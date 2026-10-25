-- Named rivalries, matched on CFBD school names. team_a sorts before team_b (ordinal), so each pair is stored once.

CREATE TABLE dbo.cfb_rivalry (
    team_a nvarchar(100) NOT NULL,
    team_b nvarchar(100) NOT NULL,
    name   nvarchar(100) NOT NULL,
    CONSTRAINT pk_cfb_rivalry PRIMARY KEY (team_a, team_b),
    CONSTRAINT ck_cfb_rivalry_order CHECK (team_a < team_b)
);

INSERT INTO dbo.cfb_rivalry (team_a, team_b, name) VALUES
    (N'Alabama', N'Auburn', N'Iron Bowl'),
    (N'Alabama', N'Tennessee', N'Third Saturday in October'),
    (N'Arizona', N'Arizona State', N'Territorial Cup'),
    (N'Army', N'Navy', N'Army–Navy Game'),
    (N'Auburn', N'Georgia', N'Deep South''s Oldest Rivalry'),
    (N'BYU', N'Utah', N'Holy War'),
    (N'California', N'Stanford', N'Big Game'),
    (N'Clemson', N'South Carolina', N'Palmetto Bowl'),
    (N'Duke', N'North Carolina', N'Victory Bell'),
    (N'Florida', N'Florida State', N'Sunshine Showdown'),
    (N'Florida', N'Georgia', N'Florida–Georgia Game'),
    (N'Florida State', N'Miami', N'Florida State–Miami'),
    (N'Georgia', N'Georgia Tech', N'Clean, Old-Fashioned Hate'),
    (N'Indiana', N'Purdue', N'Old Oaken Bucket'),
    (N'Iowa', N'Iowa State', N'Cy-Hawk Trophy'),
    (N'Kansas', N'Kansas State', N'Sunflower Showdown'),
    (N'Kentucky', N'Louisville', N'Governor''s Cup'),
    (N'Michigan', N'Michigan State', N'Paul Bunyan Trophy'),
    (N'Michigan', N'Ohio State', N'The Game'),
    (N'Minnesota', N'Wisconsin', N'Paul Bunyan''s Axe'),
    (N'Mississippi State', N'Ole Miss', N'Egg Bowl'),
    (N'Notre Dame', N'USC', N'Jeweled Shillelagh'),
    (N'Oklahoma', N'Oklahoma State', N'Bedlam'),
    (N'Oklahoma', N'Texas', N'Red River Rivalry'),
    (N'Oregon', N'Oregon State', N'Oregon–Oregon State'),
    (N'Pittsburgh', N'West Virginia', N'Backyard Brawl'),
    (N'Texas', N'Texas A&M', N'Lone Star Showdown'),
    (N'UCLA', N'USC', N'Crosstown Showdown'),
    (N'Virginia', N'Virginia Tech', N'Commonwealth Clash'),
    (N'Washington', N'Washington State', N'Apple Cup');
