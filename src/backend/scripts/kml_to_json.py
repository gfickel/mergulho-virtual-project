import json
import xml.etree.ElementTree as ET
import sys
import os

def kml_to_json(kml_path, json_path):
    """
    Converts a KML file with folders of placemarks to a JSON file.
    Output: JSON list of objects with 'name' and 'points' (list of {lat, lon}).
    """
    try:
        tree = ET.parse(kml_path)
        root = tree.getroot()
    except Exception as e:
        print(f"Error parsing KML file: {e}")
        return

    # KML usually uses this namespace
    namespace = {'kml': 'http://www.opengis.net/kml/2.2'}

    # Helper to find elements with or without namespace
    def find_all(element, tag_name):
        return element.findall(f'.//kml:{tag_name}', namespace) if '}' in root.tag else element.findall(f'.//{tag_name}')

    def find_one(element, tag_name):
        return element.find(f'kml:{tag_name}', namespace) if '}' in root.tag else element.find(f'{tag_name}')

    results = []

    # We key off folders now
    folders = find_all(root, 'Folder')

    target_folders = []
    for folder in folders:
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
                
                points = []
                for vertex in raw_coords:
                     vertex_parts = vertex.split(',')
                     if len(vertex_parts) >= 2:
                         lon = float(vertex_parts[0])
                         lat = float(vertex_parts[1])
                         points.append({"lat": lat, "lon": lon})
                
                if points:
                    results.append({
                        "name": name_id,
                        "points": points
                    })

    if not results:
        print("No valid Placemarks found.")

    try:
        with open(json_path, 'w', encoding='utf-8') as f:
            json.dump(results, f, indent=2, ensure_ascii=False)
        print(f"Successfully created {json_path} with {len(results)} entries.")
    except Exception as e:
        print(f"Error writing JSON file: {e}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python kml_to_json.py <input.kml> [output.json]")
    else:
        input_kml = sys.argv[1]
        output_json = sys.argv[2] if len(sys.argv) > 2 else input_kml.replace('.kml', '.json')
        kml_to_json(input_kml, output_json)
