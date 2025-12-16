import argparse
import csv
from pathlib import Path

from tqdm import tqdm

import firebase_admin
from firebase_admin import credentials, firestore


BASE_DIR = Path(__file__).resolve().parent.parent  # points to `backend/`
SERVICE_ACCOUNT_PATH = BASE_DIR / "serviceAccountKey.json"
CSV_PATH = Path(__file__).resolve().parent / "avistamentos_set2024.csv"


# Initialize Firebase app only once
if not firebase_admin._apps:
    cred = credentials.Certificate(str(SERVICE_ACCOUNT_PATH))
    firebase_admin.initialize_app(cred)

db = firestore.client()


class Avistamento:
    def __init__(
        self,
        registro,
        nome_popular,
        nome_cientifico,
        observador,
        classificacao_observador,
        dia_registro,
        mes_registro,
        ano_registro,
        local,
        quantidade,
        comportamento,
        tamanho_estimado,
        sexo,
        interacao,
        modo_registro,
        link_instagram,
        dia_anotacao,
        mes_anotacao,
        ano_anotacao,
        responsavel_anotacao,
        operadora_empresa_foto,
        recebido_por,
        observacao,
        outra_ID,
        concatenado,
    ):
        self.registro = registro
        self.nome_popular = nome_popular
        self.nome_cientifico = nome_cientifico
        self.observador = observador
        self.classificacao_observador = classificacao_observador
        self.dia_registro = dia_registro
        self.mes_registro = mes_registro
        self.ano_registro = ano_registro
        self.local = local
        self.quantidade = quantidade
        self.comportamento = comportamento
        self.tamanho_estimado = tamanho_estimado
        self.sexo = sexo
        self.interacao = interacao
        self.modo_registro = modo_registro
        self.link_instagram = link_instagram
        self.dia_anotacao = dia_anotacao
        self.mes_anotacao = mes_anotacao
        self.ano_anotacao = ano_anotacao
        self.responsavel_anotacao = responsavel_anotacao
        self.operadora_empresa_foto = operadora_empresa_foto
        self.recebido_por = recebido_por
        self.observacao = observacao
        self.outra_ID = outra_ID
        self.concatenado = concatenado

    def to_dict(self):
        return vars(self)


def row_to_avistamento(row: dict) -> Avistamento:
    """
    Convert a CSV DictReader row into a Sighting instance.

    The CSV header (first line) defines the keys used below.
    """
    return Avistamento(
        registro=row["registro"],
        nome_popular=row["nome_popular"],
        nome_cientifico=row["nome_cientifico"],
        observador=row["observador"],
        classificacao_observador=row["classificacao_observador"],
        dia_registro=row["dia_registro"],
        mes_registro=row["mes_registro"],
        ano_registro=row["ano_registro"],
        local=row["local"],
        quantidade=row["quantidade"],
        comportamento=row["comportamento"],
        tamanho_estimado=row["tamanho_estimado"],
        sexo=row["sexo"],
        interacao=row["interacao"],
        modo_registro=row["modo_registro"],
        link_instagram=row["link_instagram"],
        dia_anotacao=row["dia_anotacao"],
        mes_anotacao=row["mes_anotacao"],
        ano_anotacao=row["ano_anotacao"],
        responsavel_anotacao=row["responsavel_anotacao"],
        operadora_empresa_foto=row["operadora_empresa_foto"],
        recebido_por=row["Recebido_por"],
        observacao=row["observação"],
        outra_ID=row["outra_ID"],
        concatenado=row["Concatenado"],
    )


def import_avistamentos(csv_path: Path = CSV_PATH, max_linhas: int | None = None):
    """
    Read the CSV and import each line as a Sighting document into Firestore.
    """
    avistamentos_ref = db.collection("avistamentos")

    with csv_path.open(mode="r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        count = 0
        for row in tqdm(reader, desc="Importando avistamentos", unit=""):
            avistamento = row_to_avistamento(row)
            # Use `registro` as the document ID so re-running the script upserts.
            doc_id = str(avistamento.registro)
            avistamentos_ref.document(doc_id).set(avistamento.to_dict())
            count += 1
            if max_linhas is not None and count >= max_linhas:
                break

    print(f"\nImportados {count} avistamentos de {csv_path}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Importa avistamentos de tubarões/raias de um arquivo CSV para o Firestore."
    )
    parser.add_argument(
        "csv_path",
        nargs="?",
        default=str(CSV_PATH),
        help="Caminho para o arquivo CSV (padrão: avistamentos_set2024.csv).",
    )
    parser.add_argument(
        "-n",
        "--num-linhas",
        type=int,
        default=None,
        help="Número máximo de linhas do CSV a importar (padrão: importa todas).",
    )
    args = parser.parse_args()

    csv_arg = Path(args.csv_path)
    import_avistamentos(csv_path=csv_arg, max_linhas=args.num_linhas)
