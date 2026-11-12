-- CyberBot Part 3 - MySQL schema
-- NOTE: the app creates all of this automatically on first run.
-- Run this file manually only if you want to see/create it yourself
-- (MySQL Workbench: File > Open SQL Script > run with the lightning button).

CREATE DATABASE IF NOT EXISTS cyberbot_db CHARACTER SET utf8mb4;
USE cyberbot_db;

CREATE TABLE IF NOT EXISTS tasks (
    id                INT AUTO_INCREMENT PRIMARY KEY,
    title             VARCHAR(150) NOT NULL,
    description       VARCHAR(500) NOT NULL,
    reminder_date     DATETIME NULL,                    -- optional reminder
    is_completed      TINYINT(1) NOT NULL DEFAULT 0,    -- 0 = pending, 1 = done
    reminder_notified TINYINT(1) NOT NULL DEFAULT 0,    -- 1 once the bot has announced it
    created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Handy checks:
-- SELECT * FROM tasks;
-- DELETE FROM tasks;   -- start fresh
