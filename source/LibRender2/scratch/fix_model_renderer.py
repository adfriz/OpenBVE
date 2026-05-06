import os
import re

def fix_model_renderer(file_path):
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    new_content = content.replace("Models.StaticObjectStates", "StaticObjectStates")
    new_content = new_content.replace("Models.DynamicObjectStates", "DynamicObjectStates")
    new_content = new_content.replace("Models.VisibleObjects", "VisibleObjects")
    new_content = new_content.replace("Models.ObjectsSortedByStart", "ObjectsSortedByStart")
    new_content = new_content.replace("Models.ObjectsSortedByEnd", "ObjectsSortedByEnd")
    new_content = new_content.replace("Models.ObjectsSortedByStartPointer", "ObjectsSortedByStartPointer")
    new_content = new_content.replace("Models.ObjectsSortedByEndPointer", "ObjectsSortedByEndPointer")
    new_content = new_content.replace("Models.LastUpdatedTrackPosition", "LastUpdatedTrackPosition")
    
    if new_content != content:
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print(f"Fixed: {file_path}")

if __name__ == "__main__":
    fix_model_renderer("source/LibRender2/Models/ModelRenderer.cs")
