CREATE TABLE dbo.greeting (
    id         int IDENTITY(1, 1) NOT NULL CONSTRAINT pk_greeting PRIMARY KEY,
    message    nvarchar(500)      NOT NULL,
    created_at datetime2          NOT NULL CONSTRAINT df_greeting_created_at DEFAULT SYSUTCDATETIME()
);

INSERT INTO dbo.greeting (message) VALUES (N'Hello from Azure SQL!');
