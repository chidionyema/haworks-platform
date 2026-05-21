# Vault prod-mode config with Shamir seal.
#
# Auto-unsealed by entrypoint.sh using the key stored in .init.json
# on the Fly persistent volume. For production: use cloud KMS.

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
