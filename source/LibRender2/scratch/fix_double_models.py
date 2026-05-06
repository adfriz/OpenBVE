import os
import re

def fix_double_models(file_path):
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
    except UnicodeDecodeError:
        try:
            with open(file_path, 'r', encoding='latin-1') as f:
                content = f.read()
        except:
            print(f"Skipping: {file_path}")
            return

    # Collapse multiple Models. prefixes
    new_content = re.sub(r'(Models\.){2,}', 'Models.', content)
    
    # Fix method definitions in overrides
    new_content = re.sub(r'\b(virtual|override)\s+void\s+Models\.', r'\1 void ', new_content)
    new_content = re.sub(r'\b(virtual|override)\s+int\s+Models\.', r'\1 int ', new_content)

    # Fix host calls: currentHost.Models.CreateDynamicObject -> currentHost.CreateDynamicObject
    new_content = re.sub(r'(\b\w*Host\w*)\.Models\.CreateDynamicObject', r'\1.CreateDynamicObject', new_content)
    new_content = re.sub(r'(\b\w*Host\w*)\.Models\.ShowObject', r'\1.ShowObject', new_content)
    new_content = re.sub(r'(\b\w*Host\w*)\.Models\.HideObject', r'\1.HideObject', new_content)
    
    if new_content != content:
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print(f"Fixed: {file_path}")

if __name__ == "__main__":
    for root, dirs, files in os.walk("source"):
        for file in files:
            if file.endswith(".cs"):
                fix_double_models(os.path.join(root, file))
