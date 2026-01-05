import json
import csv
import os

import json
import csv
import os
import argparse

def convert_my_wildlife_to_csv():
    """
    Converts a wildlife JSON file to a flattened CSV.
    """
    parser = argparse.ArgumentParser(description='Convert wildlife JSON to CSV.')
    parser.add_argument('input_file', help='Path to the input JSON file')
    parser.add_argument('-o', '--output', help='Path to the output CSV file')

    args = parser.parse_args()

    input_file_path = args.input_file
    
    if args.output:
        output_file_path = args.output
    else:
        # Default output: replace extension with .csv
        base, _ = os.path.splitext(input_file_path)
        output_file_path = f"{base}.csv"

    print(f"Reading from: {input_file_path}")
    
    try:
        with open(input_file_path, 'r') as f:
            data = json.load(f)
    except FileNotFoundError:
        print(f"Error: File not found at {input_file_path}")
        return
    except json.JSONDecodeError:
        print(f"Error: Failed to decode JSON from {input_file_path}")
        return

    deployments = data.get('deployments', [])
    flattened_data = []

    for deployment in deployments:
        oid = deployment.get('_id', {}).get('$oid')
        title = deployment.get('title')
        locations = deployment.get('locations', [])

        for loc in locations:
            row = {
                'oid': oid,
                'title': title,
                'date': loc.get('date'),
                'latitude': loc.get('latitude'),
                'longitude': loc.get('longitude'),
                'notes': loc.get('notes')
            }
            flattened_data.append(row)

    if not flattened_data:
        print("No data found to write to CSV.")
        return

    print(f"Writing {len(flattened_data)} rows to: {output_file_path}")

    fieldnames = ['oid', 'title', 'date', 'latitude', 'longitude', 'notes']

    try:
        with open(output_file_path, 'w', newline='') as csvfile:
            writer = csv.DictWriter(csvfile, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(flattened_data)
        print("Conversion complete.")
    except IOError as e:
        print(f"Error writing to file {output_file_path}: {e}")

if __name__ == "__main__":
    convert_my_wildlife_to_csv()
