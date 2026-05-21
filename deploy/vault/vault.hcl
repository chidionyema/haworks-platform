# Vault prod-mode config with Transit auto-unseal.
#
# A persistent transit vault runs on port 8100 (same machine, file backend
# at /vault/data/transit/) as the Transit seal provider. The entrypoint
# patches the token field at runtime via sed after initializing the transit
# vault and reading its root token.

storage "raft" {
  path    = "/vault/data"
  node_id = "haworks-vault-1"
}

listener "tcp" {
  address     = "[::]:8200"
  tls_disable = "true"
}

seal "transit" {
  address         = "http://127.0.0.1:8100"
  disable_renewal = "false"
  key_name        = "autounseal"
  mount_path      = "transit/"
  tls_skip_verify = "true"
  token           = "PLACEHOLDER_PATCHED_BY_ENTRYPOINT"
}

api_addr      = "http://[::1]:8200"
cluster_addr  = "http://[::1]:8201"
ui            = false
disable_mlock = true
