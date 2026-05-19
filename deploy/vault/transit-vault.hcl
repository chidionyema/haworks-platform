# Lightweight Transit-only vault running in dev mode on the same machine.
# Its sole purpose is to provide an auto-unseal key for the production vault.
# Dev mode = no persistence needed, auto-initializes, never seals.

listener "tcp" {
  address     = "127.0.0.1:8100"
  tls_disable = "true"
}

disable_mlock = true
