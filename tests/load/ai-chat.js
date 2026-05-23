import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, lightStages, thinkTime, pseudoUuid } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const ttftLatency        = new Trend('ai_time_to_first_token', true);
const totalLatency       = new Trend('ai_total_response_time', true);
const chatSuccess        = new Rate('ai_chat_success_rate');
const chatCount          = new Counter('ai_chat_messages');
const streamErrors       = new Counter('ai_stream_errors');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    ai_chat: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: lightStages,
    },
  },
  thresholds: {
    'ai_time_to_first_token': [`p(95)<${SLO.ai_ttft_p95}`],
    'ai_total_response_time': [`p(95)<${SLO.ai_total_p95}`],
    'ai_chat_success_rate':   ['rate>0.95'],
    'http_req_failed':        ['rate<0.05'],
  },
};

// ── Conversation turns ──────────────────────────────────────────────
const CONVERSATIONS = [
  [
    'What products do you recommend for a home office setup?',
    'Can you compare the top two options you mentioned?',
    'Which one has better reviews?',
  ],
  [
    'I need help choosing a gift for someone who loves cooking.',
    'My budget is around $50. What fits?',
    'Do any of those come with a warranty?',
  ],
  [
    'Tell me about your return policy.',
    'How long does a refund typically take?',
    'Can I exchange instead of getting a refund?',
  ],
  [
    'Are there any eco-friendly products in your catalog?',
    'Which of those is the most popular?',
    'Does it ship internationally?',
  ],
];

// ── SSE response parser ─────────────────────────────────────────────
function measureStreamResponse(res) {
  // k6 does not natively parse SSE streams, but we can measure:
  // - res.timings.waiting = time to first byte (TTFT proxy)
  // - res.timings.duration = total response time
  const ttft = res.timings.waiting || res.timings.duration;
  const total = res.timings.duration;

  return { ttft, total };
}

// ── Default function ────────────────────────────────────────────────
export default function () {
  const { headers } = getAuthHeaders(BASE_URL);
  const conversation = CONVERSATIONS[Math.floor(Math.random() * CONVERSATIONS.length)];
  const sessionId = pseudoUuid();

  const chatHeaders = Object.assign({}, headers, {
    Accept: 'text/event-stream',
  });

  // ── Multi-turn conversation (3 turns) ─────────────────────────
  const messageHistory = [];

  for (let turn = 0; turn < conversation.length; turn++) {
    const userMessage = conversation[turn];
    messageHistory.push({ role: 'user', content: userMessage });

    group(`Chat Turn ${turn + 1}`, () => {
      const payload = JSON.stringify({
        sessionId,
        message: userMessage,
        history: messageHistory,
        stream: true,
      });

      const res = http.post(
        `${BASE_URL}/api/v1/ai/chat/message`,
        payload,
        {
          headers: chatHeaders,
          tags: { name: `chat_turn_${turn + 1}` },
          timeout: '30s',
        }
      );

      const ok = check(res, {
        'chat 2xx': (r) => r.status >= 200 && r.status < 300,
        'has response body': (r) => r.body && r.body.length > 0,
      });

      if (ok) {
        const metrics = measureStreamResponse(res);
        ttftLatency.add(metrics.ttft);
        totalLatency.add(metrics.total);
        chatSuccess.add(true);

        // Add assistant response to history for multi-turn context
        // Extract from SSE or plain body
        let assistantContent = '';
        if (res.body) {
          // SSE format: lines starting with "data: "
          const lines = res.body.split('\n');
          for (const line of lines) {
            if (line.startsWith('data: ')) {
              const data = line.substring(6).trim();
              if (data && data !== '[DONE]') {
                try {
                  const parsed = JSON.parse(data);
                  assistantContent += parsed.content || parsed.text || parsed.delta || '';
                } catch (_) {
                  assistantContent += data;
                }
              }
            }
          }
          // Fallback: plain JSON response
          if (!assistantContent) {
            try {
              const body = JSON.parse(res.body);
              assistantContent = body.content || body.message || body.text || '';
            } catch (_) {
              assistantContent = res.body.substring(0, 200);
            }
          }
        }

        messageHistory.push({ role: 'assistant', content: assistantContent });
      } else {
        chatSuccess.add(false);
        streamErrors.add(1);
      }

      chatCount.add(1);
    });

    // Think time between turns (user reading the response)
    if (turn < conversation.length - 1) {
      sleep(thinkTime(2, 5));
    }
  }

  // Inter-session think time
  sleep(thinkTime(3, 8));
}
