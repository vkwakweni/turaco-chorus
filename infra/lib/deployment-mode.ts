import * as fs from 'fs';
import * as path from 'path';

/**
 * Which ports run fake, and whether the service is reachable from the whole internet over HTTPS.
 * Read from a local, gitignored file (config/deployment.local.json), so the committed defaults stay
 * the demo deployment: both ports fake, plain HTTP, inbound limited to the IP allow-list.
 */
export interface DeploymentMode {
  readonly useFakeIdentityVerifier: boolean;
  readonly useFakeLogDataSource: boolean;
  readonly publicHttps: boolean;
}

export const DEFAULT_DEPLOYMENT_MODE: DeploymentMode = {
  useFakeIdentityVerifier: true,
  useFakeLogDataSource: true,
  publicHttps: false,
};

const KNOWN_KEYS = Object.keys(DEFAULT_DEPLOYMENT_MODE);

/**
 * Validates the parsed file. Unknown keys and non-boolean values are errors rather than being
 * ignored, so a typo can never silently leave a port fake or the service closed. Refuses a public
 * deployment while the identity verifier is fake: a fake verifier accepts one known credential, not a
 * real per-user check, so opening the service to the internet would only be as safe as that secret.
 */
export function parseDeploymentMode(raw: unknown): DeploymentMode {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
    throw new Error('deployment.local.json must contain a JSON object.');
  }

  const values = raw as Record<string, unknown>;
  for (const key of Object.keys(values)) {
    if (!KNOWN_KEYS.includes(key)) {
      throw new Error(`deployment.local.json has unknown key "${key}"; expected only: ${KNOWN_KEYS.join(', ')}.`);
    }
  }

  const mode: Record<string, boolean> = { ...DEFAULT_DEPLOYMENT_MODE };
  for (const key of KNOWN_KEYS) {
    if (key in values) {
      if (typeof values[key] !== 'boolean') {
        throw new Error(`deployment.local.json's "${key}" must be true or false, got ${JSON.stringify(values[key])}.`);
      }
      mode[key] = values[key] as boolean;
    }
  }

  const result = mode as unknown as DeploymentMode;
  if (result.publicHttps && result.useFakeIdentityVerifier) {
    throw new Error(
      'deployment.local.json sets "publicHttps": true while "useFakeIdentityVerifier" is still true. ' +
      'Set "useFakeIdentityVerifier": false (a real identity provider) before opening the service to the internet.',
    );
  }
  return result;
}

/** Missing file means the defaults, so deployments that never opt in are unaffected. */
export function readDeploymentMode(configDir: string = path.join(__dirname, '..', 'config')): DeploymentMode {
  const configPath = path.join(configDir, 'deployment.local.json');
  if (!fs.existsSync(configPath)) {
    return DEFAULT_DEPLOYMENT_MODE;
  }
  return parseDeploymentMode(JSON.parse(fs.readFileSync(configPath, 'utf-8')));
}
