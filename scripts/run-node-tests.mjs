import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const testsDir = path.resolve(process.cwd(), 'tests');

function listTestFiles(dirPath) {
  return fs
    .readdirSync(dirPath, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith('.test.mjs'))
    .map((entry) => path.join(dirPath, entry.name))
    .sort((left, right) => left.localeCompare(right));
}

function runTestFile(filePath) {
  const result = spawnSync(process.execPath, [filePath], {
    stdio: 'inherit',
    env: process.env
  });

  if (result.error) {
    throw result.error;
  }

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

for (const filePath of listTestFiles(testsDir)) {
  console.log(`RUN ${path.basename(filePath)}`);
  runTestFile(filePath);
}
