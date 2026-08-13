import { readdir, rm, stat, writeFile } from 'node:fs/promises';
import { basename, extname, join, relative, resolve, sep } from 'node:path';
import { spawn } from 'node:child_process';
import { createWorkerConfig } from './src/config.ts';
import { fixedBuildArtifactKey } from './src/build-artifacts.ts';
import {
  DEPLOYMENT_MANIFEST_KEY,
  deploymentManifest,
  parseDeploymentManifest,
  staleDeploymentKeys,
} from './src/deployment-manifest.ts';

interface Options {
  domain: string;
  buildDirectory: string;
  workerName: string;
  bucketName: string;
  workerOnly: boolean;
}

interface Upload {
  source: string;
  key: string;
  contentType: string;
  contentEncoding?: 'gzip' | 'br';
}

const MIME_TYPES: Readonly<Record<string, string>> = {
  '.bin': 'application/octet-stream',
  '.bundle': 'application/octet-stream',
  '.bytes': 'application/octet-stream',
  '.css': 'text/css; charset=utf-8',
  '.data': 'application/octet-stream',
  '.html': 'text/html; charset=utf-8',
  '.ico': 'image/x-icon',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.png': 'image/png',
  '.wasm': 'application/wasm',
};

function parseOptions(arguments_: string[]): Options {
  if (arguments_[0] === '--') arguments_ = arguments_.slice(1);
  const workerOnly = arguments_.includes('--worker-only');
  arguments_ = arguments_.filter(argument => argument !== '--worker-only');
  const values = new Map<string, string>();
  for (let index = 0; index < arguments_.length; index += 2) {
    const name = arguments_[index];
    const value = arguments_[index + 1];
    if (!name?.startsWith('--') || value === undefined) {
      throw new Error('Usage: pnpm run publish -- --domain <domain> --build-dir <directory> [--worker-name <name>] [--bucket-name <name>]');
    }
    values.set(name, value);
  }

  const domain = values.get('--domain')?.trim();
  const buildDirectory = values.get('--build-dir')?.trim();
  if (!domain || !buildDirectory) {
    throw new Error('--domain and --build-dir are required.');
  }
  if (!/^[a-z0-9.-]+$/u.test(domain)) throw new Error(`Invalid domain: ${domain}`);

  const defaultName = domain.toLowerCase().replace(/[^a-z0-9-]+/gu, '-').replace(/^-|-$/gu, '');
  return {
    domain,
    buildDirectory: resolve(buildDirectory),
    workerName: values.get('--worker-name') ?? defaultName,
    bucketName: values.get('--bucket-name') ?? `${defaultName}-web`,
    workerOnly,
  };
}

function run(command: string, arguments_: string[], allowFailure = false): Promise<void> {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, arguments_, { stdio: 'inherit' });
    child.once('error', rejectPromise);
    child.once('exit', code => {
      if (code === 0 || allowFailure) resolvePromise();
      else rejectPromise(new Error(`${command} exited with code ${code ?? 'unknown'}.`));
    });
  });
}

function runCaptured(command: string, arguments_: string[]): Promise<{ output: string; succeeded: boolean }> {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, arguments_, { stdio: ['ignore', 'pipe', 'ignore'] });
    let output = '';
    child.stdout.setEncoding('utf8');
    child.stdout.on('data', chunk => { output += chunk; });
    child.once('error', rejectPromise);
    child.once('exit', code => resolvePromise({ output, succeeded: code === 0 }));
  });
}

function runWithInput(command: string, arguments_: string[], input: string): Promise<void> {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, arguments_, { stdio: ['pipe', 'inherit', 'inherit'] });
    child.once('error', rejectPromise);
    child.once('exit', code => {
      if (code === 0) resolvePromise();
      else rejectPromise(new Error(`${command} exited with code ${code ?? 'unknown'}.`));
    });
    child.stdin.end(input);
  });
}

async function filesIn(directory: string): Promise<string[]> {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(entries.map(entry => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? filesIn(path) : Promise.resolve([path]);
  }));
  return nested.flat();
}

function uploadFor(buildDirectory: string, source: string): Upload {
  const key = fixedBuildArtifactKey(relative(buildDirectory, source).split(sep).join('/'));
  const compressionExtension = extname(source).toLowerCase();
  const contentEncoding = compressionExtension === '.gz'
    ? 'gzip'
    : compressionExtension === '.br'
      ? 'br'
      : undefined;
  const contentPath = contentEncoding === undefined
    ? source
    : source.slice(0, -compressionExtension.length);
  const contentType = MIME_TYPES[extname(contentPath).toLowerCase()] ?? 'application/octet-stream';
  return { source, key, contentType, contentEncoding };
}

async function previousDeploymentKeys(bucketName: string): Promise<string[]> {
  const result = await runCaptured('pnpm', [
    'exec', 'wrangler', 'r2', 'object', 'get', `${bucketName}/${DEPLOYMENT_MANIFEST_KEY}`,
    '--remote', '--pipe',
  ]);
  return result.succeeded ? parseDeploymentManifest(result.output).keys : [];
}

async function synchronizeDeployment(options: Options, uploads: Upload[]): Promise<void> {
  const keys = uploads.map(upload => upload.key);
  if (new Set(keys).size !== keys.length) throw new Error('Fixed build artifact keys are not unique.');

  const previousKeys = await previousDeploymentKeys(options.bucketName);
  await uploadFiles(options, uploads);
  for (const key of staleDeploymentKeys(previousKeys, keys)) {
    console.log(`Deleting ${key}`);
    await run('pnpm', [
      'exec', 'wrangler', 'r2', 'object', 'delete', `${options.bucketName}/${key}`, '--remote',
    ]);
  }
  await runWithInput('pnpm', [
    'exec', 'wrangler', 'r2', 'object', 'put', `${options.bucketName}/${DEPLOYMENT_MANIFEST_KEY}`,
    '--pipe', '--content-type', 'application/json; charset=utf-8',
    '--cache-control', 'no-store', '--remote',
  ], deploymentManifest(keys));
}

async function uploadFiles(options: Options, uploads: Upload[]): Promise<void> {
  let next = 0;
  const workers = Array.from({ length: Math.min(4, uploads.length) }, async () => {
    while (next < uploads.length) {
      const upload = uploads[next++];
      console.log(`Uploading ${upload.key}`);
      const arguments_ = [
        'exec', 'wrangler', 'r2', 'object', 'put', `${options.bucketName}/${upload.key}`,
        '--file', upload.source,
        '--content-type', upload.contentType,
        '--cache-control', 'public, max-age=0, must-revalidate',
        '--remote',
      ];
      if (upload.contentEncoding !== undefined) {
        arguments_.push('--content-encoding', upload.contentEncoding);
      }
      await run('pnpm', arguments_);
    }
  });
  await Promise.all(workers);
}

async function main(): Promise<void> {
  const options = parseOptions(process.argv.slice(2));
  const buildStatus = await stat(options.buildDirectory).catch(() => null);
  if (!buildStatus?.isDirectory()) throw new Error(`Build directory not found: ${options.buildDirectory}`);

  const uploads = (await filesIn(options.buildDirectory)).map(source => uploadFor(options.buildDirectory, source));
  if (!uploads.some(upload => upload.key === 'index.html')) {
    throw new Error(`index.html not found in ${options.buildDirectory}`);
  }

  if (!options.workerOnly) {
    await run('pnpm', ['exec', 'wrangler', 'r2', 'bucket', 'create', options.bucketName], true);
    await synchronizeDeployment(options, uploads);
  }

  const scriptDirectory = import.meta.dirname;
  const configPath = join(scriptDirectory, '.wrangler.generated.json');
  const config = createWorkerConfig({
    workerName: options.workerName,
    workerScript: join(scriptDirectory, 'src/worker.ts'),
    compatibilityDate: new Date().toISOString().slice(0, 10),
    domain: options.domain,
    bucketName: options.bucketName,
  });

  try {
    await writeFile(configPath, `${JSON.stringify(config, null, 2)}\n`);
    await run('pnpm', ['exec', 'wrangler', 'deploy', '--config', configPath]);
  } finally {
    await rm(configPath, { force: true });
  }

  console.log(`Deployed https://${options.domain}/ from ${basename(options.buildDirectory)}`);
}

await main();
