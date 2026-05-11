# Agent CLI strategy — survive quota walls

The wave protocol is **agent-agnostic by design**. Any non-interactive LLM CLI that accepts `--prompt "..."` works. The wave script's `cmd_run` and `cmd_launch` read two env vars to pick the CLI:

| Env var | Purpose | Default |
|---|---|---|
| `WAVE_AGENT_CMD` | CLI used for execution agents (N parallel) | `gemini --yolo` |
| `WAVE_DESIGN_AGENT_CMD` | CLI used for the design pass (1 call) | falls back to `WAVE_AGENT_CMD` |

## When Gemini quota is exhausted

Common case during heavy wave usage. Switching Gemini *models* (e.g., `gemini-3-flash-preview` → `gemini-2.5-flash`) **does not help** — the OAuth tier shares quota across all Gemini models. The actual unblocks, in order of recommendation:

### 1. Switch Gemini to API key (the cleanest paid unblock)

```bash
gemini /auth                              # interactive — choose 'API key'
# paste a Google AI Studio key from https://aistudio.google.com/app/apikey
```

Free tier: ~60 RPM, ~1500 RPD per model — usually enough for ~2 waves per day.
Paid tier: per-token billing, vastly higher quota.

No wave-script change needed. Same `gemini` binary, different auth backend.

### 2. Switch to Claude (or any other vendor's CLI)

```bash
# install Anthropic's Claude CLI (separate quota pool from Gemini)
export WAVE_AGENT_CMD='claude --dangerously-skip-permissions'
wave run docs/agent-briefs/<spec>.md
```

Each LLM vendor (Anthropic, OpenAI, Mistral, etc.) has its own quota. If Gemini is exhausted, Claude likely isn't, and vice versa. Mix-and-match across runs.

### 3. Two-tier model — pro design, flash execution

Design needs reasoning; execution is mechanical. Use a stronger model for the 1 design call, a cheaper one for the N agent calls:

```bash
export WAVE_DESIGN_AGENT_CMD='gemini --yolo --model gemini-3-pro-preview'   # strong reasoning
export WAVE_AGENT_CMD='gemini --yolo --model gemini-3-flash-preview'         # cheap parallel
wave run docs/agent-briefs/<spec>.md
```

This matters when the design pass and the parallel execution would otherwise compete for the same model's quota.

### 4. Local Gemma — zero quota, always available

For mechanical execution work that doesn't need state-of-the-art reasoning, run Gemma locally:

```bash
gemini gemma setup                        # one-time: downloads model
gemini gemma start                        # runs LiteRT-LM server on localhost

export WAVE_AGENT_CMD='gemini --yolo --model gemma-2'
wave run docs/agent-briefs/<spec>.md
```

Trade-offs:
- ✓ Zero API quota; runs offline
- ✓ Fast first-token latency (no network round-trip)
- ✗ Smaller model (2B-9B params); weaker on complex multi-file refactors
- ✗ Hardware-bound; needs enough RAM (~16GB for the 9B variant)

**Use Gemma for execution where the brief is highly directive** (signatures + skeletons + verbatim Done — per `token-efficient-briefs.md`). For complex tracks with design judgement, fall back to cloud.

### 5. Wait for OAuth reset

Free option; usually ~24h cycle. Use the time to write more wave specs.

## The two-tier strategy spelled out

The most cost-effective default for sustained heavy use:

```bash
# Strong cloud model for the one-shot design pass (low call volume, needs reasoning)
export WAVE_DESIGN_AGENT_CMD='gemini --yolo --model gemini-3-pro-preview'

# Cheap cloud model OR local Gemma for the N parallel execution agents
export WAVE_AGENT_CMD='gemini --yolo --model gemini-3-flash-preview'
# … or, when quota is tight:
# export WAVE_AGENT_CMD='gemini --yolo --model gemma-2'      # local
# export WAVE_AGENT_CMD='claude --dangerously-skip-permissions'  # different vendor
```

Persist these in `~/.zshrc` or per-project `.envrc` so every wave inherits.

## What the wave script does internally

```bash
local agent_cmd="${WAVE_AGENT_CMD:-gemini --yolo}"
local agent_bin="${agent_cmd%% *}"               # the binary name only (for `have` check)
have "$agent_bin" || die "$agent_bin CLI not found"

# … then invokes:
$agent_cmd --prompt "$built_prompt"
```

The contract any CLI must satisfy:
- accepts `--prompt "..."` on the command line
- writes files via its own tool layer (the agent's job, not ours)
- exits 0 on success, non-zero on failure

Most modern agent CLIs (Gemini, Claude, OpenAI Codex, Cody, Continue) meet this. Some need flag adjustments — for those, set `WAVE_AGENT_CMD` to the full prefix with required flags baked in.

## Anti-patterns

- **Hardcoding `gemini` in custom scripts.** Always read from `WAVE_AGENT_CMD` so the operator's choice propagates.
- **Mixing tiers within a single wave.** If T1 uses Gemini Pro and T2 uses Gemma, their outputs may not be stylistically consistent. Pick one execution model per wave.
- **Using local Gemma for the design pass.** The design pass produces the brief that 8 agents follow — its quality matters disproportionately. Gemma for execution, cloud (or API-key cloud) for design.
- **Falling back to a weaker model without revisiting the brief.** A weaker model needs a more directive brief. `wave audit-brief` should score green BEFORE switching to Gemma.
