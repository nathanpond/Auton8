-- Least-privilege role for the Flowable engine (#150).
--
-- Flowable previously connected as POSTGRES_USER — the bootstrap superuser that
-- also owns `AutoNate` and `autonate_datastores`. Anything reaching the engine's
-- datasource therefore reached the application's own data as its owner. This role
-- sees the `flowable` database and nothing else.
--
-- Roles are CLUSTER-wide, so creation races between concurrently starting
-- containers. The DO/EXCEPTION idiom is the same one PluginReaderLockdownSql uses
-- for that reason: a plain CREATE ROLE loses the race and aborts the whole script.
--
-- NOTE: docker-entrypoint-initdb.d runs ONLY on an empty data directory. An
-- existing cluster does not pick this up — see docs/DEPLOYMENT.md for the one-off.
-- The release compose applies the same SQL from its db-init service, which runs on
-- every start and therefore also covers upgrades.
DO $$
BEGIN
    CREATE ROLE flowable_app LOGIN PASSWORD 'flowable_dev_only_change_me';
EXCEPTION
    WHEN duplicate_object OR unique_violation THEN
        NULL;
END $$;

-- Owning the database is what lets Flowable create and upgrade its own schema at
-- startup, which it does itself rather than through a migration tool.
ALTER DATABASE flowable OWNER TO flowable_app;

-- CONNECT is granted to PUBLIC on every new database, so restricting this role
-- means revoking it. Guarded per database: `autonate_datastores` is created later
-- by the application, and an unguarded REVOKE against a database that does not
-- exist yet aborts the whole init script.
DO $$
DECLARE
    d TEXT;
BEGIN
    FOREACH d IN ARRAY ARRAY['AutoNate', 'autonate_datastores']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_database WHERE datname = d) THEN
            EXECUTE format('REVOKE ALL ON DATABASE %I FROM flowable_app', d);
            EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', d);
        END IF;
    END LOOP;
END $$;
