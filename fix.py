import os
import re
import sys

def fix_file(filepath):
    try:
        with open(filepath, 'r') as f:
            content = f.read()
            
        original = content

        # Fix NotEqual(Guid.Empty) in validators -> NotEmpty()
        content = re.sub(r'\.NotEqual\(\s*Guid\.Empty\s*\)', '.NotEmpty()', content)
        
        # Fix : Guid.Empty in controllers
        content = re.sub(r':\s*Guid\.Empty', ': default', content)
        content = re.sub(r'=\s*Guid\.Empty', '= default', content)
        content = re.sub(r'==\s*Guid\.Empty', '== default', content)

        if content != original:
            with open(filepath, 'w') as f:
                f.write(content)
            print(f"Fixed {filepath}")
    except Exception as e:
        print(f"Error processing {filepath}: {e}")

for root, _, files in os.walk('src'):
    for file in files:
        if file.endswith('.cs'):
            fix_file(os.path.join(root, file))
