# Vault prod-mode config with Transit auto-unseal.
#
# A lightweight dev-mode vault runs on port 8100 (same machine) as the
# Transit seal backend. On every restart, prod vault calls the Transit
# endpoint to decrypt its master key — no Shamir unseal keys, no operator
# intervention, no timing races with CI.

storage "raft" {
  path    = "/vault/data"
  node_id = "haworks-vault-1"
}

listener "tcp" {
  address     = "[::]:8200"
  tls_disable = "true"
}

# Transit auto-unseal: the local dev-mode vault on :8100 provides the
# encryption key. The TRANSIT_VAULT_TOKEN env is set by the entrypoint
# after starting the transit vault.
seal "transit" {
  address         = "http://127.0.0.1:8100"
  disable_renewal = "false"
  key_name        = "autounseal"
  mount_path      = "transit/"
  tls_skip_verify = "true"
  # Token is injected via VAULT_SEAL_TRANSIT_TOKEN env by entrypoint.sh
}

api_addr      = "http://[::1]:8200"
cluster_addr  = "http://[::1]:8201"
ui            = false
disable_mlock = true
