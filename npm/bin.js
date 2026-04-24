#!/usr/bin/env node
const { spawnSync } = require("child_process");
const path = require("path");
const fs = require("fs");

const bin = path.join(__dirname, "bin", process.platform === "win32" ? "bse-code.exe" : "bse-code");

if (!fs.existsSync(bin)) {
  console.error("bse-code binary not found. Try reinstalling: npm install -g bse-code");
  process.exit(1);
}

const result = spawnSync(bin, process.argv.slice(2), { stdio: "inherit" });
process.exit(result.status ?? 1);
