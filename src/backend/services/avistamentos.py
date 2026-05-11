from typing import Optional, List, Tuple, Dict, Any
from urllib.parse import urlencode
from google.cloud import firestore
from google.cloud.firestore_v1.base_query import FieldFilter
from database import db



def query_avistamentos(
    page: int = 1,
    page_size: int = 10,
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
    local: Optional[str] = None,
    nome_popular: Optional[str] = None,
) -> Tuple[List[Dict[str, Any]], int, int, bool]:
    """
    Função comum para buscar avistamentos do Firestore com paginação e filtros.
    Retorna uma tupla: (items, page, page_size, has_more)
    """
    # Sanitiza parâmetros básicos
    page = max(page, 1)
    page_size = max(min(page_size, 100), 1)  # limita page_size entre 1 e 100

    offset = (page - 1) * page_size

    query = _build_query(dia_registro, mes_registro, ano_registro, local, nome_popular)

    query = query.offset(offset).limit(page_size)
    docs = query.stream()
    items = [doc.to_dict() for doc in docs]

    has_more = len(items) == page_size

    return items, page, page_size, has_more


def build_avistamentos_url(
    page: int,
    page_size: int,
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
    local: Optional[str] = None,
    nome_popular: Optional[str] = None,
) -> str:
    """
    Constrói a URL para a lista de avistamentos com os parâmetros de query.
    """
    params: List[Tuple[str, str]] = [
        ("page", str(page)),
        ("page_size", str(page_size)),
    ]
    if dia_registro is not None:
        params.append(("dia_registro", str(dia_registro)))
    if mes_registro is not None:
        params.append(("mes_registro", str(mes_registro)))
    if ano_registro is not None:
        params.append(("ano_registro", str(ano_registro)))
    if local:
        params.append(("local", local))
    if nome_popular:
        params.append(("nome_popular", nome_popular))

    return "/avistamentos?" + urlencode(params)


def count_avistamentos(
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
    local: Optional[str] = None,
    nome_popular: Optional[str] = None,
) -> int:
    """
    Conta o número total de avistamentos que correspondem aos filtros.
    """
    query = _build_query(dia_registro, mes_registro, ano_registro, local, nome_popular)
    aggregate_query = query.count()
    results = aggregate_query.get()
    return results[0][0].value


def _build_query(
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
    local: Optional[str] = None,
    nome_popular: Optional[str] = None,
):
    """
    Helper para construir a query base com filtros.
    """
    query = db.collection("avistamentos").order_by("registro")

    # Date filters: stored as strings in Firestore.
    if dia_registro is not None:
        query = query.where(filter=FieldFilter("dia_registro", "==", str(dia_registro)))
    if mes_registro is not None:
        query = query.where(filter=FieldFilter("mes_registro", "==", str(mes_registro)))
    if ano_registro is not None:
        query = query.where(filter=FieldFilter("ano_registro", "==", str(ano_registro)))
    if local:
        query = query.where(filter=FieldFilter("local", "==", local))
    if nome_popular:
        query = query.where(filter=FieldFilter("nome_popular", "==", nome_popular))

    return query
