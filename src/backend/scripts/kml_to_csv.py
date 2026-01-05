
import csv
import xml.etree.ElementTree as ET
import sys
import os

def kml_to_csv(kml_path, csv_path):
    """
    Converts a KML file with folders of placemarks to a CSV file.
    CSV columns: id, latitude, longitude
    """
    try:
        tree = ET.parse(kml_path)
        root = tree.getroot()
    except Exception as e:
        print(f"Error parseing KML file: {e}")
        return

    # KML usually uses this namespace
    namespace = {'kml': 'http://www.opengis.net/kml/2.2'}
    
    # Sometimes namespaces are different or absent, so we might need a more robust search or 
    # try to strip namespaces. For now, we try the standard one.
    # If the root tag doesn't contain the namespace (e.g. {http://www.opengis.net/kml/2.2}kml),
    # we might need to adjust.
    
    # Helper to find elements with or without namespace
    def find_all(element, tag_name):
        return element.findall(f'.//kml:{tag_name}', namespace) if '}' in root.tag else element.findall(f'.//{tag_name}')

    def find_one(element, tag_name):
        return element.find(f'kml:{tag_name}', namespace) if '}' in root.tag else element.find(f'{tag_name}')

    data_rows = []
    
    # We key off folders now
    folders = find_all(root, 'Folder')
    
    target_folders = []
    for folder in folders:
        # Check for id attribute. Attributes are not namespaced usually in KML XML parsing unless explicitly so.
        # But 'id' is a standard XML attribute. ElementTree stores it in .attrib
        if folder.get('id') == "results":
            target_folders.append(folder)
            
    if not target_folders:
        print("No folder with id='results' found.")
        return

    # We are looking for Placemarks inside the target folders
    all_placemarks = []
    for folder in target_folders:
        all_placemarks.extend(find_all(folder, 'Placemark'))
    
    for pm in all_placemarks:
        name_elem = find_one(pm, 'name')
        
        # We look for LinearRing inside MultiGeometry or fallback to direct LinearRing
        multi_geo = find_one(pm, 'MultiGeometry')
        linear_ring = None
        if multi_geo is not None:
             linear_ring = find_one(multi_geo, 'LinearRing')
        else:
             linear_ring = find_one(pm, 'LinearRing')
        
        if name_elem is not None and linear_ring is not None:
            coord_elem = find_one(linear_ring, 'coordinates')
            if coord_elem is not None and coord_elem.text:
                # KML coordinates in LinearRing are space separated tuples
                # identifying the vertices of the polygon
                # e.g. "lon,lat,alt lon,lat,alt ..."
                raw_coords = coord_elem.text.strip().split()
                name_id = name_elem.text.strip()
                
                for vertex in raw_coords:
                     vertex_parts = vertex.split(',')
                     if len(vertex_parts) >= 2:
                         lon = vertex_parts[0]
                         lat = vertex_parts[1]
                         data_rows.append([name_id, lat, lon])

    if not data_rows:
        print("No valid Placemarks found.")
    
    try:
        with open(csv_path, 'w', newline='', encoding='utf-8') as f:
            writer = csv.writer(f)
            writer.writerow(['id', 'latitude', 'longitude'])
            writer.writerows(data_rows)
        print(f"Successfully created {csv_path} with {len(data_rows)} entries.")
    except Exception as e:
        print(f"Error writing CSV file: {e}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python kml_to_csv.py <input.kml> [output.csv]")
    else:
        input_kml = sys.argv[1]
        output_csv = sys.argv[2] if len(sys.argv) > 2 else input_kml.replace('.kml', '.csv')
        kml_to_csv(input_kml, output_csv)
