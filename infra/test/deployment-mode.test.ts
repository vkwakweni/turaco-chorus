import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { DEFAULT_DEPLOYMENT_MODE, parseDeploymentMode, readDeploymentMode } from '../lib/deployment-mode';

describe('parseDeploymentMode', () => {
  test('an empty object gives the demo defaults', () => {
    expect(parseDeploymentMode({})).toEqual({
      useFakeIdentityVerifier: true,
      useFakeLogDataSource: true,
      publicHttps: false,
    });
  });

  test('real adapters without public access is allowed', () => {
    expect(parseDeploymentMode({ useFakeIdentityVerifier: false, useFakeLogDataSource: false })).toEqual({
      useFakeIdentityVerifier: false,
      useFakeLogDataSource: false,
      publicHttps: false,
    });
  });

  test('public access with a real identity verifier is allowed', () => {
    expect(parseDeploymentMode({ useFakeIdentityVerifier: false, publicHttps: true }).publicHttps).toBe(true);
  });

  test('public access with the fake identity verifier is refused', () => {
    expect(() => parseDeploymentMode({ publicHttps: true })).toThrow(/useFakeIdentityVerifier/);
    expect(() => parseDeploymentMode({ publicHttps: true, useFakeIdentityVerifier: true })).toThrow(/real identity provider/);
  });

  test('an unknown key is an error, so a typo cannot leave a port fake', () => {
    expect(() => parseDeploymentMode({ useFakeIdentityVerifer: false })).toThrow(/unknown key "useFakeIdentityVerifer"/);
  });

  test('a non-boolean value is an error', () => {
    expect(() => parseDeploymentMode({ useFakeLogDataSource: 'false' })).toThrow(/must be true or false/);
  });

  test('anything other than an object is an error', () => {
    expect(() => parseDeploymentMode(null)).toThrow(/JSON object/);
    expect(() => parseDeploymentMode([])).toThrow(/JSON object/);
  });
});

describe('readDeploymentMode', () => {
  let dir: string;

  beforeEach(() => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'deployment-mode-'));
  });

  afterEach(() => {
    fs.rmSync(dir, { recursive: true, force: true });
  });

  test('a missing file gives the demo defaults', () => {
    expect(readDeploymentMode(dir)).toEqual(DEFAULT_DEPLOYMENT_MODE);
  });

  test('reads and validates the file', () => {
    fs.writeFileSync(path.join(dir, 'deployment.local.json'), JSON.stringify({ useFakeIdentityVerifier: false, publicHttps: true }));
    expect(readDeploymentMode(dir)).toEqual({
      useFakeIdentityVerifier: false,
      useFakeLogDataSource: true,
      publicHttps: true,
    });
  });

  test('a file that fails validation is an error, not a silent default', () => {
    fs.writeFileSync(path.join(dir, 'deployment.local.json'), JSON.stringify({ publicHttps: true }));
    expect(() => readDeploymentMode(dir)).toThrow(/useFakeIdentityVerifier/);
  });
});
