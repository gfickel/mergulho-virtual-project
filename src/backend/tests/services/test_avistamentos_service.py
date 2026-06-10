"""Tests for the pure helpers in services.avistamentos (no Firestore needed)."""

from services.avistamentos import build_avistamentos_url


def test_build_url_no_filters():
    assert build_avistamentos_url(2, 10) == "/avistamentos?page=2&page_size=10"


def test_build_url_with_all_filters_urlencodes_values():
    url = build_avistamentos_url(
        1,
        25,
        dia_registro=5,
        mes_registro=6,
        ano_registro=2026,
        local="Baía do Sancho",
        nome_popular="Tubarão-martelo",
    )
    assert url == (
        "/avistamentos?page=1&page_size=25"
        "&dia_registro=5&mes_registro=6&ano_registro=2026"
        "&local=Ba%C3%ADa+do+Sancho&nome_popular=Tubar%C3%A3o-martelo"
    )


def test_build_url_zero_day_is_kept_but_empty_local_dropped():
    # dia_registro uses an `is not None` check (0 survives); local uses
    # truthiness ("" is dropped). Pin both behaviors.
    url = build_avistamentos_url(1, 10, dia_registro=0, local="")
    assert "dia_registro=0" in url
    assert "local" not in url
