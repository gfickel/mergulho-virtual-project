# Fotos próprias dos artigos

Drop a `.jpg` / `.jpeg` / `.png` here and `make articles` copies it to
`src/app/MergulhoVirtual/Assets/Resources/Articles/` and writes a Sprite `.meta` beside it (cloned
from an existing one with a fresh GUID), so Unity imports it correctly **without anyone opening the
Editor**. Reference it from an article as `Articles/<nome-do-arquivo-sem-extensão>`.

Nothing is copied by `make articles-check` — validation accepts a file staged here as resolvable so
CI passes before the install has happened.

Photos already in the project do **not** belong here: use `Beaches/<nome>` and `Animals/<nome>`
directly. This folder is currently empty on purpose (the placeholder articles reuse existing
photos); this README only keeps the directory in git.

⚠️ Every image needs a credit line in the article that uses it —
`Foto: Autor / Licença (Fonte)`. That is a licence condition, not a caption style.
