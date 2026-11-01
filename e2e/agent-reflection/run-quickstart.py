#!/usr/bin/env python3
"""Run the agent-reflection browser journey on the server's customer Compose package."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import urllib.request
import urllib.error

import yaml

ROOT = Path(__file__).resolve().parents[2]
INSTALL = ROOT / 'e2e' / '.agent-reflection-private'
SERVER_SOURCE = ROOT / 'e2e' / 'server-source'
COMPOSE_FILES = ('compose.yaml', 'compose.executor.yaml', 'compose.executor.image.yaml', 'compose.auth.override.yaml')
SERVER_IMAGE = os.environ['HONUA_CONSOLE_E2E_SERVER_IMAGE']
CONSOLE_IMAGE = os.environ['HONUA_CONSOLE_E2E_CONSOLE_IMAGE']
EXECUTOR_IMAGE = os.environ['HONUA_REFLECTION_EXECUTOR_IMAGE']


def run(args, *, cwd=ROOT, env=None, timeout=600):
    result = subprocess.run(args, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f'{args[0]} failed with exit code {result.returncode}')
    return result.stdout.decode().strip()


def compose(*args):
    command = ['docker', 'compose', '--project-directory', str(INSTALL)]
    for file in COMPOSE_FILES:
        command.extend(['-f', str(INSTALL / file)])
    return run([*command, *args])


def request(url, key, data):
    payload = json.dumps(data).encode()
    req = urllib.request.Request(url, data=payload, headers={'X-API-Key': key, 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f'Admin setup returned HTTP {error.code}') from None


def wait_idp():
    import ssl
    context = ssl._create_unverified_context()  # Test-only local Keycloak certificate.
    url = 'https://127.0.0.1:18443/realms/honua/.well-known/openid-configuration'
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(url, context=context, timeout=5) as response:
                if response.status == 200:
                    return
        except (OSError, urllib.error.URLError):
            time.sleep(2)
    raise RuntimeError('The local OIDC realm did not become ready')


def wait_console():
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen('http://127.0.0.1:15274/version.json', timeout=5) as response:
                if response.status == 200:
                    return
        except (OSError, urllib.error.URLError):
            time.sleep(2)
    raise RuntimeError('The Console did not become ready')


def runtime_identity():
    observed = {}
    for name, requested, services in (
        ('server', SERVER_IMAGE, ('honua', 'compose-manager')),
        ('console', CONSOLE_IMAGE, ('console',)),
        ('executor', EXECUTOR_IMAGE, ('compose-executor',)),
    ):
        image_id = run(['docker', 'image', 'inspect', requested, '--format', '{{.Id}}'])
        revision = run(['docker', 'image', 'inspect', requested, '--format', '{{index .Config.Labels "org.opencontainers.image.revision"}}'])
        digests = json.loads(run(['docker', 'image', 'inspect', requested, '--format', '{{json .RepoDigests}}']) or 'null') or []
        if '@' in requested and requested.split('@', 1)[1] not in [item.split('@', 1)[-1] for item in digests]:
            raise RuntimeError(f'{name} digest is absent from the local image receipt')
        for service in services:
            container = compose('ps', '-q', service)
            if not container or run(['docker', 'inspect', container, '--format', '{{.Image}}']) != image_id:
                raise RuntimeError(f'{service} is not running the selected image')
        observed[name] = {'requested': requested, 'imageId': image_id, 'repoDigests': digests, 'sourceRevision': revision}
    observed['suiteSourceRevision'] = os.environ.get('GITHUB_SHA')
    output = ROOT / 'artifacts/studio-customer/agent-runtime-identity.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(observed, indent=2) + '\n')


def setup():
    if INSTALL.exists():
        raise RuntimeError('The isolated quickstart directory already exists')
    run([sys.executable, str(SERVER_SOURCE / 'scripts/docker/package_quickstart.py'),
         '--server-image', SERVER_IMAGE, '--executor-image', EXECUTOR_IMAGE, '--worker-image', SERVER_IMAGE,
         '--version', '2026.1-rc.2', '--output', str(INSTALL)])
    # Reserve distinct local ports before the product initializer writes its private defaults.
    (INSTALL / '.env').write_text('HONUA_HTTP_PORT=18090\nHONUA_GRPC_PORT=18091\nHONUA_MANAGER_PORT=18092\n')
    os.chmod(INSTALL / '.env', 0o600)
    run([sys.executable, str(INSTALL / 'quickstart.py'), '--init-only'], cwd=INSTALL)
    values = dict(line.split('=', 1) for line in (INSTALL / '.env').read_text().splitlines() if line and not line.startswith('#'))
    auth = INSTALL / 'auth'
    (auth / 'certs').mkdir(parents=True)
    (auth / 'realm').mkdir()
    for file in ('kc.crt', 'kc.key'):
        shutil.copyfile(ROOT / 'e2e/playwright/live-auth/certs' / file, auth / 'certs' / file)
    # Keycloak runs as uid 1000 in CI. Its private key stays inside the private install directory.
    run(['sudo', 'chown', '1000:0', str(auth / 'certs/kc.key')])
    os.chmod(auth / 'certs/kc.key', 0o640)
    for file in (ROOT / 'e2e/playwright/live-auth/realm').glob('*.json'):
        parsed = json.loads(file.read_text())
        rendered = json.dumps(parsed).replace(':5274', ':15274').replace(':8443', ':18443')
        (auth / 'realm' / file.name).write_text(rendered)
    source = yaml.safe_load((ROOT / 'e2e/docker-compose.auth.yml').read_text())['services']
    keycloak = source['keycloak']
    keycloak['ports'] = ['127.0.0.1:18443:18443']
    keycloak['environment']['KC_HTTPS_PORT'] = '18443'
    keycloak['environment']['KC_HOSTNAME'] = keycloak['environment']['KC_HOSTNAME'].replace(':8443', ':18443')
    keycloak['volumes'] = ['./auth/certs:/opt/keycloak/certs:ro', './auth/realm:/opt/keycloak/data/import:ro']
    server_auth = source['honua-server']['environment']
    server_auth['PUBLIC_BASE_URL'] = 'http://127.0.0.1:15274'
    server_auth['Oidc__Generic__Authority'] = server_auth['Oidc__Generic__Authority'].replace(':8443', ':18443')
    override = {'services': {
        'keycloak': keycloak,
        'honua': {'environment': server_auth, 'volumes': ['./auth/certs:/certs:ro']},
        'compose-manager': {'environment': server_auth, 'volumes': ['./auth/certs:/certs:ro']},
        'console': {'image': CONSOLE_IMAGE, 'ports': ['127.0.0.1:15274:8080'],
                    'environment': {'ASPNETCORE_ENVIRONMENT': 'Development', 'ASPNETCORE_HTTP_PORTS': '8080',
                                    'HONUA_SERVER_BASE_URL': 'http://compose-manager:8080',
                                    'Honua__Console__Auth__Mode': 'Dev'}},
    }}
    (INSTALL / COMPOSE_FILES[-1]).write_text(json.dumps(override))
    os.chmod(INSTALL / COMPOSE_FILES[-1], 0o600)
    compose('up', '-d', '--wait', '--wait-timeout', '300')
    wait_idp()
    wait_console()
    runtime_identity()
    manager, server = 'http://127.0.0.1:18092', 'http://127.0.0.1:18090'
    def new_key(base, admin, name):
        value = request(base + '/api/v1/admin/api-keys/', admin, {'name': name, 'permissions': ['admin:*']})
        return value['data']
    manager_proposer = new_key(manager, values['HONUA_MANAGER_ADMIN_PASSWORD'], 'agent-proposer')
    manager_approver = new_key(manager, values['HONUA_MANAGER_ADMIN_PASSWORD'], 'human-approver')
    server_proposer = new_key(server, values['HONUA_ADMIN_PASSWORD'], 'agent-importer')
    server_approver = new_key(server, values['HONUA_ADMIN_PASSWORD'], 'human-publisher-approver')
    actors = {
        'HONUA_REFLECTION_MANAGER_PROPOSER': manager_proposer['key'],
        'HONUA_REFLECTION_MANAGER_APPROVER': manager_approver['key'],
        'HONUA_REFLECTION_SERVER_PROPOSER': server_proposer['key'],
        'HONUA_REFLECTION_SERVER_APPROVER': server_approver['key'],
        'HONUA_REFLECTION_MANAGER_ACTOR': 'apikey:api-key:' + manager_proposer['apiKey']['id'],
        'HONUA_REFLECTION_SERVER_ACTOR': 'apikey:api-key:' + server_proposer['apiKey']['id'],
    }
    connection = {'name': 'agent-reflection-db', 'host': 'postgres', 'port': 5432, 'databaseName': 'honua',
                  'username': 'honua', 'password': values['POSTGRES_PASSWORD'], 'provider': 'postgis',
                  'sslRequired': False, 'sslMode': 'Disable'}
    request(server + '/api/v1/admin/connections/', actors['HONUA_REFLECTION_SERVER_APPROVER'], connection)
    realm = json.loads(next((auth / 'realm').glob('*.json')).read_text())
    customer = next(user for user in realm['users'] if user['username'] == 'customer')
    actors.update({
        'HONUA_REFLECTION_CUSTOMER_USER': 'customer',
        'HONUA_REFLECTION_CUSTOMER_PASSWORD': next(c['value'] for c in customer['credentials'] if c['type'] == 'password'),
        'HONUA_REFLECTION_CONSOLE_URL': 'http://127.0.0.1:15274',
        'HONUA_REFLECTION_MANAGER_URL': manager, 'HONUA_REFLECTION_SERVER_URL': server,
        'HONUA_REFLECTION_COMPOSE_DIR': str(INSTALL), 'HONUA_REFLECTION_CONNECTION_NAME': 'agent-reflection-db',
        'HONUA_CONSOLE_E2E_CONSOLE_IMAGE': CONSOLE_IMAGE,
        'PLAYWRIGHT_JSON_OUTPUT_FILE': str(ROOT / 'artifacts/studio-customer/agent-reflection-results.json'),
    })
    return actors


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in ('run', 'clean'):
        raise RuntimeError('Specify run or clean')
    if sys.argv[1] == 'clean':
        if INSTALL.exists() and all((INSTALL / file).exists() for file in COMPOSE_FILES):
            compose('down', '-v')
        return
    env = dict(os.environ, **setup())
    # Only this child receives test credentials; do not add them to workflow output or artifacts.
    result = subprocess.run(['./node_modules/.bin/playwright', 'test', '--config=playwright.live.config.ts',
                             '--project=agent-reflection', '--reporter=list,json'],
                            cwd=ROOT / 'e2e/playwright', env=env)
    if result.returncode:
        raise RuntimeError(f'Agent reflection Playwright failed with exit code {result.returncode}')


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.TimeoutExpired) as error:
        # Our RuntimeError messages contain only fixed text, status codes and command names.
        detail = f' {error}' if type(error) is RuntimeError else ''
        print('Agent quickstart journey failed.' + detail, file=sys.stderr)
        sys.exit(1)
