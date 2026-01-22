-- Create ChordCharts table
CREATE TABLE IF NOT EXISTS "ChordCharts" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ChordCharts" PRIMARY KEY AUTOINCREMENT,
    "SongId" INTEGER NOT NULL,
    "Key" TEXT NOT NULL,
    "Content" TEXT NOT NULL,
    "Capo" TEXT NULL,
    "TimeSignature" TEXT NULL,
    "Format" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NULL,
    "CreatedByUserId" INTEGER NULL,
    CONSTRAINT "FK_ChordCharts_Songs_SongId" FOREIGN KEY ("SongId") REFERENCES "Songs" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ChordCharts_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

-- Create indexes
CREATE INDEX IF NOT EXISTS "IX_ChordCharts_CreatedByUserId" ON "ChordCharts" ("CreatedByUserId");
CREATE INDEX IF NOT EXISTS "IX_ChordCharts_SongId_Key" ON "ChordCharts" ("SongId", "Key");