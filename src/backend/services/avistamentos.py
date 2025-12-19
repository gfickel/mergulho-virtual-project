from typing import Optional, List, Tuple, Dict, Any
from google.cloud import firestore
from database import db



def query_avistamentos(
    page: int = 1,
    page_size: int = 10,
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
) -> Tuple[List[Dict[str, Any]], int, int, bool]:
    """
    Função comum para buscar avistamentos do Firestore com paginação e filtros.
    Retorna uma tupla: (items, page, page_size, has_more)
    """
    # Sanitiza parâmetros básicos
    page = max(page, 1)
    page_size = max(min(page_size, 100), 1)  # limita page_size entre 1 e 100

    offset = (page - 1) * page_size

    query = db.collection("avistamentos").order_by("registro")

    # Filtros opcionais por data de registro (valores vêm como int; no Firestore são strings)
    if dia_registro is not None:
        query = query.where("dia_registro", "==", str(dia_registro))
    if mes_registro is not None:
        query = query.where("mes_registro", "==", str(mes_registro))
    if ano_registro is not None:
        query = query.where("ano_registro", "==", str(ano_registro))

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
) -> str:
    """
    Constrói a URL para a lista de avistamentos com os parâmetros de query.
    """
    params = []
    params.append(f"page={page}")
    params.append(f"page_size={page_size}")
    if dia_registro is not None:
        params.append(f"dia_registro={dia_registro}")
    if mes_registro is not None:
        params.append(f"mes_registro={mes_registro}")
    if ano_registro is not None:
        params.append(f"ano_registro={ano_registro}")

    return "/avistamentos?" + "&".join(params)
