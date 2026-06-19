import os
import tarfile
import tempfile
import shutil
import re

def parse_guid(meta_path):
    with open(meta_path, 'r', encoding='utf-8') as f:
        for line in f:
            if line.startswith('guid:'):
                return line.split(':')[1].strip()
    return None

def main():
    root_dir = os.path.abspath('.')
    package_name = 'ZeyWinAdsSDK.unitypackage'
    prefix = 'Assets/ZeyWinAds/'

    # Ignore .git and tools
    ignores = ['.git', 'tools', 'pack.py', package_name]

    with tempfile.TemporaryDirectory() as temp_dir:
        for dirpath, dirnames, filenames in os.walk(root_dir):
            # filter ignores
            dirnames[:] = [d for d in dirnames if d not in ignores]
            
            for filename in filenames:
                if filename in ignores:
                    continue
                
                if filename.endswith('.meta'):
                    meta_path = os.path.join(dirpath, filename)
                    asset_path = meta_path[:-5] # remove .meta
                    
                    rel_path = os.path.relpath(asset_path, root_dir)
                    # For root meta file itself? Wait, .meta without asset?
                    # The root folder ZeyWinAds doesn't have a meta file here because we are packing its contents.
                    # Actually, the repo itself has .meta files for its top-level items.
                    # So rel_path is something like 'Runtime/AdManager.cs'
                    
                    guid = parse_guid(meta_path)
                    if not guid:
                        continue
                        
                    guid_dir = os.path.join(temp_dir, guid)
                    os.makedirs(guid_dir, exist_ok=True)
                    
                    # 1. Copy meta
                    shutil.copy2(meta_path, os.path.join(guid_dir, 'asset.meta'))
                    
                    # 2. Copy asset if exists and is file
                    if os.path.exists(asset_path) and os.path.isfile(asset_path):
                        shutil.copy2(asset_path, os.path.join(guid_dir, 'asset'))
                        
                    # 3. Create pathname
                    pathname_content = prefix + rel_path.replace(os.sep, '/')
                    with open(os.path.join(guid_dir, 'pathname'), 'w', encoding='utf-8', newline='\n') as f:
                        f.write(pathname_content + '\n')

        # Add a special meta for the root folder if we want to be clean, but Unity usually handles missing parent folder metas.
        
        # Now compress temp_dir to tar.gz
        print(f"Creating {package_name}...")
        with tarfile.open(package_name, "w:gz") as tar:
            # tar.add will add the directory structure. We want the guids at the root of the tar.
            for item in os.listdir(temp_dir):
                item_path = os.path.join(temp_dir, item)
                tar.add(item_path, arcname=item)
                
        print("Done!")

if __name__ == '__main__':
    main()
