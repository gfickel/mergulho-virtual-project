# Sharks of Fernando de Noronha — species list & 3D model candidates

## Sharks documented at Fernando de Noronha

Drawn from Brazilian dive sources and the Important Shark & Ray Areas (ISRA) factsheet for Noronha:

| # | PT-BR | EN / Scientific | Status at Noronha |
|---|---|---|---|
| 1 | Tubarão-lixa (lambaru) | Atlantic Nurse Shark — *Ginglymostoma cirratum* | Abundant; reproductive/nursery area |
| 2 | Tubarão cabeça-de-cesto (bico-fino) | Caribbean Reef Shark — *Carcharhinus perezi* | Abundant; only known regular resting site in the region |
| 3 | Tubarão-limão | Lemon Shark — *Negaprion brevirostris* | Abundant; aggregates seasonally to feed on sardines |
| 4 | Tubarão-tigre | Tiger Shark — *Galeocerdo cuvier* | Increasingly frequent — **already in the app** |
| 5 | Tubarão-martelo | Hammerhead — *Sphyrna sp.* | Sporadic, oceanic — **already in the app** |
| 6 | Tubarão-baleia | Whale Shark — *Rhincodon typus* | Sporadic, oceanic |
| 7 | Tubarão-azul | Blue Shark — *Prionace glauca* | Detected via eDNA metabarcoding |

## 3D model candidates

Animated/rigged shark models are scarcer than static ones on Sketchfab — each listing below is flagged with what it actually is so you don't download a cranium expecting a swim cycle. **Verify the Animations tab on each Sketchfab page before downloading**; look for a non-zero number next to "Animations" in the model's right-hand panel.

### 1. Tubarão-lixa (Nurse Shark)
- [Nurse Shark — CatfishYAY](https://sketchfab.com/3d-models/nurse-shark-8499d62be1ab42b09aa2226d2529de12) — full-body model, check Animations tab.
- [Nurse Shark Stylized Lowpoly — sharkingaround](https://sketchfab.com/3d-models/nurse-shark-stylized-lowpoly-9a808bb241d74812aeadad66bb4e5c43) — lowpoly, good for mobile.
- Skip the [Florida Museum fetal scan](https://sketchfab.com/3d-models/nurse-shark-ginglymostomatidae-5c161f0b17e24c3a9e347429f135e3da) and [cranium](https://sketchfab.com/3d-models/nurse-shark-ginglymostoma-cranium-b159ef1a963d429f9197013636e33005) — anatomy only, no body.

### 2. Tubarão cabeça-de-cesto (Caribbean Reef Shark)
- [Model 54A — DigitalLife3D](https://sketchfab.com/3d-models/model-54a-caribbean-reef-shark-210302e0e0e74c36834fb00aaa14c7c2) — high-quality scan-derived, free for non-profit; static body (no swim cycle).
- [Caribbean Reef Shark Low Poly — Cami](https://sketchfab.com/3d-models/caribbean-reef-shark-low-poly-23e869008f784faaaf10b65c00064452) — lowpoly, check rig status.

### 3. Tubarão-limão (Lemon Shark)
- [Lemon Shark — rohr3dsolutions](https://sketchfab.com/3d-models/lemon-shark-856e54e8db9749b284d9708ac96746a0) — paid (royalty-free), quad topology, designed for underwater games; check animations.
- [Animated Shark — Optic_idealist](https://sketchfab.com/3d-models/shark-8bcd4d861bd84e87b2832e83c9cb898b) — generic shark but explicitly described as animated (graceful swim); could double as a stand-in.
- Skip the [Holliday Lab jaw muscles model](https://sketchfab.com/3d-models/lemon-shark-negaprion-brevirostris-jaw-muscles-8c09f5c2149d4a88b5f9e86cd6d99263) — research anatomy.

### 4. Tubarão-tigre
Already in app at [Assets/Models/tiger-shark-galeocerdo-cuvier/](../src/app/MergulhoVirtual/Assets/Models/tiger-shark-galeocerdo-cuvier/).

### 5. Tubarão-martelo
Already in app at [Assets/Prefabs/Tubarão Martelo.prefab](../src/app/MergulhoVirtual/Assets/Prefabs/Tubarão%20Martelo.prefab).

If you want to upgrade:
- [Hammerhead — Nyilonelycompany](https://sketchfab.com/3d-models/hammerhead-shark-e032fb8159fd44aca4ffae11efafadad) ships FBX/Unity/USDZ with 4K PBR.
- [Model 73A — DigitalLife3D](https://sketchfab.com/3d-models/model-73a-great-hammerhead-shark-77d52f2b0e084fe7bcefbc86b920f080) is the scan-derived sibling of the tiger shark you already use.

### 6. Tubarão-baleia (Whale Shark)
- [Whale Shark With Remoras — Chromasie](https://sketchfab.com/3d-models/whale-shark-with-remoras-1959a10da1fa4543a231c5f7cd098d95) — explicitly animated (rigged + morph + animation in 3ds Max for a VR game). Strongest match for "animated."
- [Whale Shark — rstr_tv](https://sketchfab.com/3d-models/whale-shark-rhincodon-typus-2ba1c81ea3a347c88a392bc65554c524) — rigged, Cinema 4D source.
- [Whale Shark Lowpoly — sharkingaround](https://sketchfab.com/3d-models/whale-shark-rhincodon-typus-lowpoly-929601bdafe64e1da6f48328e82daaad) — same author as the lowpoly nurse shark, useful if you want a consistent style across species.

### 7. Tubarão-azul (Blue Shark)
- [Blue Shark CC0 — ffish.asia](https://sketchfab.com/3d-models/cc0-blue-shark-prionace-glauca-f2af470413744d109c5e1d6fad8fe992) — CC0, no attribution required; verify animation.
- [Blue Shark — josluat91](https://sketchfab.com/3d-models/blue-shark-1f7eb5907076486ca114d51da1bda579) — alternate option.

## Suggested next step

For each model you choose, before downloading: open the Sketchfab page, scroll to the right panel, and confirm "Animations" shows ≥1. Then follow the existing workflow in [CLAUDE.md → Adding a new shark species](../CLAUDE.md) — the same Sketchfab-FBX gotchas apply (Rig→Generic, Loop Time per clip, Extract Materials → URP/Lit). Where animations are missing, the cleanest fallback is to retarget your existing tiger shark's swim animation onto the new mesh in Blender, since both will be Generic-rigged.

## Sources

- [Animais de Fernando de Noronha — fernandodenoronha.org](https://fernandodenoronha.org/animais-de-fernando-de-noronha-conheca-as-especies-mais-iconicas-da-ilha/)
- [Noronha Important Shark & Ray Area — sharkrayareas.org](https://sharkrayareas.org/portfolio-item/noronha-isra/)
- [Fernando de Noronha Shark Project Final Report — Conservation Leadership Programme](https://www.conservationleadershipprogramme.org/wp-content/uploads/2024/12/Final-Report_Fernando-de-Noronha.pdf)
- [10 dicas para nadar com tubarões — Atalaia Noronha](https://blog.atalaianoronha.com.br/tubaroes-em-fernando-de-noronha/)
- [Berçário de tubarões em Noronha — FAPESP](https://agencia.fapesp.br/bercario-de-tubaroes-em-noronha/1531)
- [Sketchfab — sharks tag](https://sketchfab.com/tags/sharks)
