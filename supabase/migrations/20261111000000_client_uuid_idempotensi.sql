-- Kunci idempotensi sinkron: client_uuid dibuat Android sekali per surat
-- dan dipakai ulang saat retry, sehingga POST yang masuk server tapi
-- responsnya hilang tidak lagi membuat surat dobel di register.
-- Idenston: UNIQUE(tenant_id, client_uuid) -- baris tanpa uuid tetap diizinkan.

ALTER TABLE antrean_surat
    ADD COLUMN IF NOT EXISTS client_uuid uuid;

CREATE UNIQUE INDEX IF NOT EXISTS idx_antrean_tenant_client_uuid
    ON antrean_surat (tenant_id, client_uuid)
    WHERE client_uuid IS NOT NULL;
