import logging

from config import DEBUG_MODE

logger = logging.getLogger("backend.debug")


class _FakeDocSnapshot:
    def __init__(self, doc_id: str):
        self.id = doc_id
        self.exists = False

    def to_dict(self):
        return {}


class _FakeAggregateResult:
    """Mimics the shape `count_xxx` reads: results[0][0].value."""
    def __init__(self, value: int = 0):
        self.value = value

    def __getitem__(self, _):
        return self


class _FakeAggregateQuery:
    def get(self):
        return _FakeAggregateResult(0)


class _FakeDocRef:
    def __init__(self, collection: str, doc_id: str):
        self._collection = collection
        self._doc_id = doc_id

    def get(self):
        logger.info("[DEBUG] firestore.get %s/%s", self._collection, self._doc_id)
        return _FakeDocSnapshot(self._doc_id)

    def set(self, data):
        logger.info("[DEBUG] firestore.set %s/%s data=%s", self._collection, self._doc_id, data)

    def update(self, data):
        logger.info("[DEBUG] firestore.update %s/%s data=%s", self._collection, self._doc_id, data)

    def delete(self):
        logger.info("[DEBUG] firestore.delete %s/%s", self._collection, self._doc_id)


class _FakeQuery:
    def __init__(self, collection: str):
        self._collection = collection

    def order_by(self, *_args, **_kwargs):
        return self

    def where(self, *_args, **_kwargs):
        return self

    def offset(self, *_args, **_kwargs):
        return self

    def limit(self, *_args, **_kwargs):
        return self

    def stream(self):
        logger.info("[DEBUG] firestore.stream %s -> []", self._collection)
        return iter(())

    def count(self):
        return _FakeAggregateQuery()


class _FakeCollection(_FakeQuery):
    def document(self, doc_id: str):
        return _FakeDocRef(self._collection, doc_id)


class _FakeFirestoreClient:
    def collection(self, name: str):
        return _FakeCollection(name)


if DEBUG_MODE:
    logging.basicConfig(level=logging.INFO)
    logger.warning("BACKEND_DEBUG enabled: Firestore writes are stubbed and only logged.")
    db = _FakeFirestoreClient()
else:
    from google.cloud import firestore
    db = firestore.Client.from_service_account_json('./serviceAccountKey.json')
