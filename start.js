// src/start.js
import { spawn } from 'child_process';
import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const args = process.argv.slice(2);
let runtime = 'node';
let tsFile = null;
let extraArgs = [];

// Parse arguments
for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg.startsWith('--runtime=')) {
        runtime = arg.split('=')[1];
    } else if (arg.startsWith('-r=')) {
        runtime = arg.split('=')[1];
    } else if (arg.endsWith('.ts') || arg.endsWith('.js')) {
        tsFile = arg;
    } else {
        extraArgs.push(arg);
    }
}

if (!tsFile) {
    console.error('Usage: node start.js <ts-file> [--runtime=node|bun|deno] [args...]');
    console.error('Example: node start.js examples/winforms/clock-app/clock-app.ts');
    console.error('Example: node start.js app.ts --runtime=deno');
    process.exit(1);
}

const filePath = path.resolve(tsFile);

if (!fs.existsSync(filePath)) {
    console.error(`Error: File not found: ${filePath}`);
    process.exit(1);
}

const runtimeFlags = {
    node: ['--experimental-transform-types'],
    bun: [],
    deno: ['run', '--allow-all', '--unstable-node-globals']
};

const runtimeArgs = runtimeFlags[runtime] || [];
const runtimeCmd = runtime;

console.log(`Running with ${runtime}: ${tsFile}`);

const proc = spawn(runtimeCmd, [...runtimeArgs, filePath, ...extraArgs], {
    stdio: 'inherit',
    cwd: process.cwd(),
    env: process.env
});

proc.on('exit', (code) => {
    process.exit(code);
});

proc.on('error', (err) => {
    console.error(`Failed to start ${runtime}:`, err.message);
    process.exit(1);
});
