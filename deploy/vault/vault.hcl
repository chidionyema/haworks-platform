# Vault prod-mode config with Shamir seal (auto-unsealed by entrypoint.sh).
#
# The entrypoint reads the unseal key from /vault/data/.init.json (persisted
# on the Fly volume) and auto-unseals on every restart. For a real production
# deployment, add a `seal "awskms"` or `seal "gcpckms"` stanza instead.

storage "raft" {
  path    = "/vault/data"
  node_id = "haworks-vault-1"
}

listener "tcp" {
  address     = "[::]:8200"
  tls_disable = "true"
}

api_addr      = "http://[::1]:8200"
cluster_addr  = "http://[::1]:8201"
ui            = false
disable_mlock = true
