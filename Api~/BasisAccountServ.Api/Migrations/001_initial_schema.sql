-- Central identity schema foundation. No public account endpoints are exposed yet.
CREATE TABLE IF NOT EXISTS accounts (
    account_id UUID PRIMARY KEY,
    username VARCHAR(24) NOT NULL,
    username_normalized VARCHAR(24) NOT NULL UNIQUE,
    password_verifier TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    disabled_at TIMESTAMPTZ NULL
);
CREATE TABLE IF NOT EXISTS basis_servers (
    server_id UUID PRIMARY KEY,
    server_name VARCHAR(100) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    disabled_at TIMESTAMPTZ NULL
);
-- A DID is a cryptographic identity derived from an Ed25519 public key.
-- It is globally linked to an AccountServ account only after proof of possession.
CREATE TABLE IF NOT EXISTS account_did_keys (
    account_id UUID NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    did VARCHAR(128) NOT NULL CHECK (did LIKE 'did:key:z%'),
    verified_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (account_id, did),
    UNIQUE (did)
);
CREATE TABLE IF NOT EXISTS account_basis_links (
    account_id UUID NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    server_id UUID NOT NULL REFERENCES basis_servers(server_id) ON DELETE CASCADE,
    basis_did VARCHAR(128) NOT NULL CHECK (basis_did LIKE 'did:key:z%'),
    linked_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (account_id, server_id),
    UNIQUE (server_id, basis_did),
    FOREIGN KEY (account_id, basis_did)
        REFERENCES account_did_keys (account_id, did)
        ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS basis_did_link_challenges (
    challenge_id UUID PRIMARY KEY,
    account_id UUID NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    did VARCHAR(128) NOT NULL CHECK (did LIKE 'did:key:z%'),
    nonce_hash BYTEA NOT NULL UNIQUE,
    verifier_context TEXT NOT NULL,
    signed_payload BYTEA NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    consumed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS server_roles (
    account_id UUID NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    server_id UUID NOT NULL REFERENCES basis_servers(server_id) ON DELETE CASCADE,
    role VARCHAR(24) NOT NULL CHECK (role IN ('user', 'moderator', 'admin')),
    granted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    granted_by UUID NULL REFERENCES accounts(account_id) ON DELETE SET NULL,
    PRIMARY KEY (account_id, server_id)
);
CREATE TABLE IF NOT EXISTS oauth_clients (
    client_id VARCHAR(128) PRIMARY KEY,
    client_secret_hash TEXT NULL,
    display_name VARCHAR(120) NOT NULL,
    redirect_uris JSONB NOT NULL DEFAULT '[]'::jsonb,
    allowed_scopes JSONB NOT NULL DEFAULT '["openid","profile"]'::jsonb,
    is_confidential BOOLEAN NOT NULL DEFAULT FALSE,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS oauth_authorization_codes (
    code_hash BYTEA PRIMARY KEY,
    client_id VARCHAR(128) NOT NULL REFERENCES oauth_clients(client_id) ON DELETE CASCADE,
    account_id UUID NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    redirect_uri TEXT NOT NULL,
    code_challenge TEXT NOT NULL,
    code_challenge_method VARCHAR(8) NOT NULL CHECK (code_challenge_method = 'S256'),
    scopes JSONB NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    consumed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS oauth_refresh_tokens (
    token_hash BYTEA PRIMARY KEY,
    client_id VARCHAR(128) NOT NULL REFERENCES oauth_clients(client_id) ON DELETE CASCADE,
    account_id UUID NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    scopes JSONB NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_account_basis_links_basis_did ON account_basis_links (basis_did);
CREATE INDEX IF NOT EXISTS ix_basis_did_link_challenges_expiry ON basis_did_link_challenges (expires_at);
CREATE INDEX IF NOT EXISTS ix_server_roles_server_role ON server_roles (server_id, role);
CREATE INDEX IF NOT EXISTS ix_oauth_codes_expiry ON oauth_authorization_codes (expires_at);
CREATE INDEX IF NOT EXISTS ix_oauth_refresh_tokens_account ON oauth_refresh_tokens (account_id, client_id);
