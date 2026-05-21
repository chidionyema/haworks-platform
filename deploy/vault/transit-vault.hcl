# Lightweight Transit-only vault with persistent file backend.
# Its sole purpose is to provide an auto-unseal key for the production vault.
# Data is persisted to /vault/data/transit/ on the Fly volume so the
# encryption key survives container restarts.

storage "file" {
  path = "/vault/data/transit"
}

listener "tcp" {
  address     = "127.0.0.1:8100"
  tls_disable = "true"
}

disable_mlock = true
ui            = false
