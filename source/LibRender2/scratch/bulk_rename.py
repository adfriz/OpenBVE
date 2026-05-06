import os

def replace_in_files(directory, search_text, replace_text):
    for root, dirs, files in os.walk(directory):
        for file in files:
            if file.endswith(".cs"):
                file_path = os.path.join(root, file)
                try:
                    with open(file_path, 'r', encoding='utf-8') as f:
                        content = f.read()
                    
                    if search_text in content:
                        # Use word boundary replacement if possible, or just string replace if careful
                        # For C#, word boundary is usually sufficient
                        import re
                        new_content = re.sub(r'\b' + re.escape(search_text) + r'\b', replace_text, content)
                        
                        if new_content != content:
                            with open(file_path, 'w', encoding='utf-8') as f:
                                f.write(new_content)
                            print(f"Updated: {file_path}")
                except Exception as e:
                    print(f"Error processing {file_path}: {e}")

if __name__ == "__main__":
    replace_in_files("source", "BaseRenderer", "RendererCore")
