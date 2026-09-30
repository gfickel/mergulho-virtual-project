# Estimated Backend Infrastructure Cost — Mergulho Virtual

Estimate for hosting the FastAPI backend (currently at [src/backend/](src/backend/)) on GCP, including Google Cloud Storage for media and Firestore for sighting metadata.

## Assumptions

- **Region**: `southamerica-east1` (São Paulo) — closest to Fernando de Noronha. `us-central1` is ~30% cheaper but adds ~150 ms latency.
- **Media volume**: ~100 items (videos + images), ~10 GB total.
- **Users**: ~50 active pilot users.
- **Traffic**: a few hundred requests/day, ~10–20 GB/month media egress (50 users × ~3 video views + image browsing).
- **VM**: e2-small (2 vCPU shared, 2 GB RAM), running 24/7.

## Per-component breakdown — São Paulo region

| Component | Spec | Monthly |
|---|---|---|
| **Compute Engine VM** | e2-small, 24/7, sustained-use discount applied | **~$14** |
| Boot disk | 10 GB pd-balanced | ~$1.70 |
| Static external IP | Optional, recommended once off LAN IP | ~$2.92 |
| **Cloud Storage** | 10 GB Standard | **~$0.20** |
| GCS Class A/B ops | Uploads + signed-URL reads at pilot scale | <$0.10 |
| **GCS egress to internet** | ~10–20 GB/mo at $0.12/GB | **~$1.20–2.40** |
| **Firestore** | Reads/writes/storage at pilot scale | **$0** (free tier: 50k reads/day, 20k writes/day, 1 GiB storage) |
| **Total** | | **~$20–22 / month** |

## What dominates as you grow

- **Egress, not storage.** GCS storage is $0.020/GB/mo in São Paulo — 10 GB is rounding error. But every video stream the app pulls costs $0.12/GB out. A single 50 MB video viewed 1000 times = 50 GB egress = $6.
- **VM is the floor.** ~$15/mo is the irreducible cost — it doesn't shrink with low traffic. If you want pay-per-request and tolerate cold starts, **Cloud Run** can drop to ~$0 idle (containerize the FastAPI app), paying only for actual request CPU-seconds. Worth considering for a pilot where the app is idle most of the day.

## Scaling outlook

| User base | Approx egress | Total monthly |
|---|---|---|
| 50 (pilot) | ~10–20 GB | ~$20 |
| 500 | ~50 GB | ~$25 |
| 5,000 | ~500 GB | ~$75 |

At 1 TB+ egress, switching to Cloudflare R2 ($0 egress) + a non-GCP VM (Hetzner/DigitalOcean) becomes meaningfully cheaper, but you're nowhere near that.

## Two practical tips before you flip the switch

1. **Use `us-central1` unless latency matters.** ~30% cheaper compute + $0.11/GB egress (vs $0.12). For an AR app where users see thumbnails after a few hundred ms either way, the savings are real.
2. **Don't put videos behind signed URLs that bypass caching.** Currently [generate_signed_url](src/backend/services/storage.py#L54) issues a fresh signed URL every page render — the URL itself differs each time, defeating browser/CDN caching. For videos, either make the bucket objects public-read with long-lived URLs, or front GCS with Cloud CDN ($0.02–0.08/GB cached egress vs $0.12 origin egress). At 10 GB media this barely matters; at 100 GB it's a meaningful cut.

## Bottom line

**~$20/month at pilot scale**, scaling roughly with egress (i.e. how much video users actually watch).
