import os
import re

def fix_car_section(file_path):
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    new_content = content.replace("currentHost.Models.CreateDynamicObject", "currentHost.CreateDynamicObject")
    
    if new_content != content:
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print(f"Fixed: {file_path}")

if __name__ == "__main__":
    fix_car_section("source/LibRender2/Trains/CarSection.cs")
