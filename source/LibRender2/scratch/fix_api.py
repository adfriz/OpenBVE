import os
import re

def fix_api(file_path):
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    new_content = content.replace("currentHost.Models.CreateDynamicObject", "currentHost.CreateDynamicObject")
    new_content = new_content.replace("currentHost.Models.ShowObject", "currentHost.ShowObject")
    new_content = new_content.replace("currentHost.Models.HideObject", "currentHost.HideObject")
    new_content = new_content.replace("Program.CurrentHost.Models.CreateDynamicObject", "Program.CurrentHost.CreateDynamicObject")
    
    if new_content != content:
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print(f"Fixed: {file_path}")

if __name__ == "__main__":
    fix_api("source/OpenBveApi/Objects/ObjectTypes/WorldObject.cs")
    fix_api("source/OpenBveApi/Objects/ObjectTypes/AnimatedObject/AnimatedObject.cs")
