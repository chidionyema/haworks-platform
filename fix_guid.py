import os
import re

def fix_file(filepath):
    try:
        with open(filepath, 'r') as f:
            content = f.read()
            
        original = content

        # Fix back default -> Guid.Empty for equality and assignment
        content = re.sub(r': default', ': Guid.Empty', content)
        content = re.sub(r'= default', '= Guid.Empty', content)
        content = re.sub(r'== default', '== Guid.Empty', content)
        # But wait! default is used for CancellationToken cancellationToken = default
        # We shouldn't globally replace `= default`.
        
    except Exception as e:
        pass

