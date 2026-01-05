import argparse
import csv
from pathlib import Path

from tqdm import tqdm

import firebase_admin
from firebase_admin import credentials, firestore


BASE_DIR = Path(__file__).resolve().parent.parent  # points to `backend/`
SERVICE_ACCOUNT_PATH = BASE_DIR / "serviceAccountKey.json"
# Default to the output from the conversion script
CSV_PATH = BASE_DIR / "services" / "my_wildlife_noronha_sharks.csv"


# Initialize Firebase app only once
if not firebase_admin._apps:
    cred = credentials.Certificate(str(SERVICE_ACCOUNT_PATH))
    firebase_admin.initialize_app(cred)

db = firestore.client()


class Telemetry:
    def __init__(
        self,
        oid,
        title,
        date,
        latitude,
        longitude,
        notes,
    ):
        self.oid = oid
        self.title = title
        self.date = date
        self.latitude = latitude
        self.longitude = longitude
        self.notes = notes

    def to_dict(self):
        return vars(self)


def row_to_telemetry(row: dict) -> Telemetry:
    """
    Convert a CSV DictReader row into a Telemetry instance.
    """
    return Telemetry(
        oid=row["oid"],
        title=row["title"],
        date=int(row["date"]) if row["date"] else None,
        latitude=float(row["latitude"]) if row["latitude"] else 0.0,
        longitude=float(row["longitude"]) if row["longitude"] else 0.0,
        notes=row["notes"],
    )


def import_telemetry(csv_path: Path = CSV_PATH, max_linhas: int | None = None):
    """
    Read the CSV and import each line as a document into 'telemetria' collection in Firestore.
    """
    collection_ref = db.collection("telemetria")

    if not csv_path.exists():
        print(f"Error: CSV file not found at {csv_path}")
        return

    with csv_path.open(mode="r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        count = 0
        
        # Use tqdm for progress bar
        for row in tqdm(reader, desc="Importing telemetry", unit=""):
            telemetry = row_to_telemetry(row)
            # Create a new document reference with an auto-generated ID
            collection_ref.add(telemetry.to_dict())
            count += 1
            if max_linhas is not None and count >= max_linhas:
                break

    print(f"\nImported {count} telemetry records from {csv_path}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Imports telemetry data from a CSV file to Firestore."
    )
    parser.add_argument(
        "csv_path",
        nargs="?",
        default=str(CSV_PATH),
        help="Path to the CSV file (default: services/my_wildlife_noronha_sharks.csv).",
    )
    parser.add_argument(
        "-n",
        "--num-lines",
        type=int,
        default=None,
        help="Maximum number of lines to import (default: all).",
    )
    args = parser.parse_args()

    csv_arg = Path(args.csv_path)
    # Check if the path is absolute, if not, verify relative to current cwd or script dir?
    # The script uses absolute paths for defaults, user input might be relative.
    # We will assume user input paths are correct as is or relative to CWD.
    
    import_telemetry(csv_path=csv_arg, max_linhas=args.num_lines)
