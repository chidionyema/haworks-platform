import re

def f(path, r_from, r_to):
    with open(path, 'r') as f:
        c = f.read()
    c = re.sub(r_from, r_to, c)
    with open(path, 'w') as f:
        f.write(c)

f('src/Notifications/Notifications.Infrastructure/Persistence/Preferences/PreferencesRepository.cs', r'\.Result', 'await ')
f('src/Notifications/Notifications.Infrastructure/Persistence/Preferences/PreferencesRepository.cs', r'GetAllForUserAsync\(Guid userId\)', 'GetAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)')
f('src/Notifications/Notifications.Infrastructure/Persistence/Suppression/SuppressionRepository.cs', r'AddAsync\(Suppression suppression\)', 'AddAsync(Suppression suppression, CancellationToken cancellationToken = default)')

with open('src/Notifications/Notifications.Application/Commands/SendNotificationCommand.cs', 'r') as file:
    content = file.read()
    # Find SaveChangesAsync and PublishAsync and swap them
    # Heuristic: just swap the order if they are close
    content = re.sub(
        r'(await _dbContext\.SaveChangesAsync\(.*?\);\s*)(await _publishEndpoint\.Publish\(.*?\);)',
        r'\2\1', content, flags=re.DOTALL
    )
with open('src/Notifications/Notifications.Application/Commands/SendNotificationCommand.cs', 'w') as file:
    file.write(content)
