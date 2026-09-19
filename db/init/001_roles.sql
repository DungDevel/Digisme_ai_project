-- Chạy 1 lần khi volume Postgres mới tạo (chạy trước pos-api)
-- pos_user: quyền full cho pos-api (EF Core Migrate)
-- ai_reader: read-only trên schema public, cho ai-service

DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'pos_user') THEN
    CREATE ROLE pos_user LOGIN PASSWORD 'pos_pass';
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ai_reader') THEN
    CREATE ROLE ai_reader LOGIN PASSWORD 'ai_reader_pass';
  END IF;
END $$;

GRANT CONNECT ON DATABASE digism_pos TO pos_user, ai_reader;

-- PG15+: schema public không còn CREATE mặc định cho non-owner
GRANT USAGE, CREATE ON SCHEMA public TO pos_user;
GRANT USAGE ON SCHEMA public TO ai_reader;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO pos_user;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO ai_reader;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO pos_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO pos_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO pos_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO ai_reader;
