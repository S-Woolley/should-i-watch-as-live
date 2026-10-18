CREATE TABLE greeting (
    id         integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    message    text        NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

INSERT INTO greeting (message) VALUES ('Hello from Postgres!');
