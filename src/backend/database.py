from google.cloud import firestore

# Initialize Firestore client
db = firestore.Client.from_service_account_json('./serviceAccountKey.json')
