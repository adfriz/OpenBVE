import os
import re

def replace_in_files(directory, replacements):
    for root, dirs, files in os.walk(directory):
        for file in files:
            if file.endswith(".cs"):
                file_path = os.path.join(root, file)
                try:
                    with open(file_path, 'r', encoding='utf-8') as f:
                        content = f.read()
                    
                    new_content = content
                    for search_text, replace_text in replacements.items():
                        new_content = re.sub(r'\b' + re.escape(search_text) + r'\b', replace_text, new_content)
                    
                    if new_content != content:
                        with open(file_path, 'w', encoding='utf-8') as f:
                            f.write(new_content)
                        print(f"Updated: {file_path}")
                except Exception as e:
                    print(f"Error processing {file_path}: {e}")

if __name__ == "__main__":
    replacements = {
        "StaticObjectStates": "Models.StaticObjectStates",
        "DynamicObjectStates": "Models.DynamicObjectStates",
        "VisibleObjects": "Models.VisibleObjects",
        "ObjectsSortedByStart": "Models.ObjectsSortedByStart",
        "ObjectsSortedByEnd": "Models.ObjectsSortedByEnd",
        "ObjectsSortedByStartPointer": "Models.ObjectsSortedByStartPointer",
        "ObjectsSortedByEndPointer": "Models.ObjectsSortedByEndPointer",
        "LastUpdatedTrackPosition": "Models.LastUpdatedTrackPosition",
        "InitializeVisibility": "Models.InitializeVisibility",
        "CreateDynamicObject": "Models.CreateDynamicObject"
    }
    replace_in_files("source", replacements)
