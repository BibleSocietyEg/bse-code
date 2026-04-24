#!/usr/bin/env node
// Downloads the correct platform binary from GitHub Releases on install.

const https = require("https");
const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const VERSION = require("./package.json").version;
const REPO = "BibleSocietyEg/bse-code"; // <-- update this
const BIN_DIR = path.join(__dirname, "bin");
const BIN_PATH = path.join(BIN_DIR, process.platform === "win32" ? "bse-code.exe" : "bse-code");

function getPlatformAsset() {
  const { platform, arch } = process;
  const map = {
    "win32-x64":   `bse-code-win-x64.exe`,
    "linux-x64":   `bse-code-linux-x64`,
    "linux-arm64": `bse-code-linux-arm64`,
    "darwin-x64":  `bse-code-osx-x64`,
    "darwin-arm64":`bse-code-osx-arm64`,
  };
  const key = `${platform}-${arch}`;
  if (!map[key]) {
    throw new Error(`Unsupported platform: ${key}. Supported: ${Object.keys(map).join(", ")}`);
  }
  return map[key];
}

function download(url, dest) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(dest);
    const get = (u) => {
      https.get(u, (res) => {
        if (res.statusCode === 301 || res.statusCode === 302) {
          return get(res.headers.location);
        }
        if (res.statusCode !== 200) {
          return reject(new Error(`Download failed: HTTP ${res.statusCode} for ${u}`));
        }
        res.pipe(file);
        file.on("finish", () => file.close(resolve));
      }).on("error", reject);
    };
    get(url);
  });
}

async function main() {
  const asset = getPlatformAsset();
  const url = `https://github.com/${REPO}/releases/download/v${VERSION}/${asset}`;

  if (!fs.existsSync(BIN_DIR)) fs.mkdirSync(BIN_DIR, { recursive: true });

  console.log(`bse-code: downloading ${asset} from GitHub Releases...`);
  await download(url, BIN_PATH);

  if (process.platform !== "win32") {
    fs.chmodSync(BIN_PATH, 0o755);
  }

  console.log("bse-code: installed successfully.");
}

main().catch((err) => {
  console.error("bse-code install failed:", err.message);
  process.exit(1);
});
