import { randomUUID } from "node:crypto";

const config = {
  controlPlaneBaseUrl: process.env.OWLP_RELEASE_SMOKE_CONTROL_PLANE_URL ?? "http://127.0.0.1:5180",
  gatewayBaseUrl: process.env.OWLP_RELEASE_SMOKE_GATEWAY_URL ?? "http://127.0.0.1:5181",
  schedulerBaseUrl: process.env.OWLP_RELEASE_SMOKE_SCHEDULER_URL ?? "http://127.0.0.1:5182",
  adminUsername: process.env.OWLP_RELEASE_SMOKE_ADMIN_USERNAME ?? "admin",
  adminPassword: process.env.OWLP_RELEASE_SMOKE_ADMIN_PASSWORD ?? "change-local-bootstrap-admin-password",
  newAdminPassword: process.env.OWLP_RELEASE_SMOKE_ADMIN_NEW_PASSWORD ?? "ReleaseReadiness!234",
  timeoutMs: Number(process.env.OWLP_RELEASE_SMOKE_TIMEOUT_MS ?? 10000)
};

const controlPlaneApi = `${config.controlPlaneBaseUrl}/api/v1`;

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function percentile(values, target) {
  const sorted = [...values].sort((left, right) => left - right);
  const index = Math.min(sorted.length - 1, Math.max(0, Math.ceil(sorted.length * target) - 1));
  return sorted[index];
}

async function request(url, { method = "GET", token, body, expectedStatus = 200 } = {}) {
  const headers = {};
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), config.timeoutMs);
  const started = performance.now();

  try {
    const response = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal
    });
    const durationMs = performance.now() - started;
    const contentType = response.headers.get("content-type") ?? "";
    const payload = contentType.includes("application/json")
      ? await response.json()
      : await response.text();

    if (response.status !== expectedStatus) {
      throw new Error(`Expected ${expectedStatus} from ${method} ${url}, got ${response.status}: ${JSON.stringify(payload)}`);
    }

    return {
      durationMs,
      headers: response.headers,
      payload
    };
  } finally {
    clearTimeout(timeout);
  }
}

function requireCorrelationHeader(response, name) {
  assert(response.headers.get("x-correlation-id"), `${name} did not return X-Correlation-ID`);
}

async function verifyOperationalEndpoint(name, url, expectedSubstring) {
  const response = await request(url);
  requireCorrelationHeader(response, name);
  if (expectedSubstring) {
    assert(
      String(response.payload).includes(expectedSubstring),
      `${name} did not include expected payload marker '${expectedSubstring}'`
    );
  }
}

async function measureReadLatency(token, url, iterations) {
  const timings = [];
  for (let index = 0; index < iterations; index += 1) {
    const response = await request(url, { token });
    requireCorrelationHeader(response, `read iteration ${index + 1}`);
    timings.push(response.durationMs);
  }

  return timings;
}

async function main() {
  await verifyOperationalEndpoint("control-plane ready", `${config.controlPlaneBaseUrl}/health/ready`);
  await verifyOperationalEndpoint("gateway ready", `${config.gatewayBaseUrl}/health/ready`);
  await verifyOperationalEndpoint("scheduler ready", `${config.schedulerBaseUrl}/health/ready`);
  await verifyOperationalEndpoint("control-plane metrics", `${config.controlPlaneBaseUrl}/metrics`, "owlprotect_auth_attempts_total");
  await verifyOperationalEndpoint("gateway metrics", `${config.gatewayBaseUrl}/metrics`, "owlprotect_gateway_heartbeats_total");
  await verifyOperationalEndpoint("scheduler metrics", `${config.schedulerBaseUrl}/metrics`, "owlprotect_scheduler_cycles_total");
  await verifyOperationalEndpoint("gateway diagnostics", `${config.gatewayBaseUrl}/diagnostics`);
  await verifyOperationalEndpoint("scheduler diagnostics", `${config.schedulerBaseUrl}/diagnostics`);

  const bootstrap = await request(`${controlPlaneApi}/bootstrap`);
  requireCorrelationHeader(bootstrap, "bootstrap");
  assert(typeof bootstrap.payload.requiresPasswordChange === "boolean", "Bootstrap status payload was incomplete.");

  const login = await request(`${controlPlaneApi}/auth/admin/login`, {
    method: "POST",
    body: {
      username: config.adminUsername,
      password: config.adminPassword
    }
  });
  requireCorrelationHeader(login, "admin login");
  assert(login.durationMs < 1500, `Admin login exceeded 1.5s baseline: ${login.durationMs.toFixed(1)}ms`);

  const adminToken = login.payload.tokens.accessToken;
  let currentAdminPassword = config.adminPassword;

  if (bootstrap.payload.requiresPasswordChange || bootstrap.payload.requiresMfaEnrollment) {
    const readDenied = await request(`${controlPlaneApi}/policies`, {
      token: adminToken,
      expectedStatus: 403
    });
    assert(readDenied.payload.errorCode === "bootstrap_admin_incomplete", "Bootstrap admin guard did not block pre-rotation reads.");
  }

  if (bootstrap.payload.requiresPasswordChange) {
    await request(`${controlPlaneApi}/admins/default/password`, {
      method: "POST",
      token: adminToken,
      body: {
        currentPassword: config.adminPassword,
        newPassword: config.newAdminPassword
      }
    });
    currentAdminPassword = config.newAdminPassword;
  }

  if (bootstrap.payload.requiresMfaEnrollment) {
    await request(`${controlPlaneApi}/admins/default/mfa`, {
      method: "POST",
      token: adminToken
    });
  }

  const policies = await request(`${controlPlaneApi}/policies`, { token: adminToken });
  const gateways = await request(`${controlPlaneApi}/gateways`, { token: adminToken });
  const operations = await request(`${controlPlaneApi}/operations/diagnostics`, { token: adminToken });
  const users = await request(`${controlPlaneApi}/users`, { token: adminToken });
  const providers = await request(`${controlPlaneApi}/auth/providers`, { token: adminToken });

  [policies, gateways, operations, users, providers].forEach((response, index) =>
    requireCorrelationHeader(response, `authenticated read ${index + 1}`));

  assert(Array.isArray(policies.payload) && policies.payload.length > 0, "Expected at least one policy.");
  assert(Array.isArray(gateways.payload) && gateways.payload.length > 0, "Expected at least one gateway.");
  assert(Array.isArray(users.payload) && users.payload.length > 0, "Expected at least one user.");

  const testUser = users.payload.find((user) => user.username === "user");
  assert(testUser, "Seeded test user was not available for smoke validation.");

  await request(`${controlPlaneApi}/users/${testUser.id}/enable`, {
    method: "POST",
    token: adminToken
  });

  const userLogin = await request(`${controlPlaneApi}/auth/user/login`, {
    method: "POST",
    body: {
      username: "user"
    }
  });
  const userToken = userLogin.payload.tokens.accessToken;
  requireCorrelationHeader(userLogin, "user login");

  const deviceName = `release-smoke-${randomUUID().slice(0, 8)}`;
  const enrolledDevice = await request(`${controlPlaneApi}/auth/client/devices/enroll`, {
    method: "POST",
    token: userToken,
    body: {
      deviceName,
      city: "New York",
      country: "United States",
      publicIp: "203.0.113.200",
      hardwareKey: `hw-${deviceName}`,
      serialNumber: `SER-${deviceName}`,
      operatingSystem: "Windows 11 24H2",
      enrollmentKind: "Bootstrap",
      managed: true
    }
  });
  requireCorrelationHeader(enrolledDevice, "device enroll");
  const enrolledDeviceRecord = enrolledDevice.payload.device;
  assert(enrolledDeviceRecord && enrolledDeviceRecord.id, "Device enrollment response did not include a device record.");
  assert(enrolledDevice.payload.requiresApproval === true, "New device enrollment should require approval in the release smoke flow.");

  const approveDenied = await request(`${controlPlaneApi}/devices/${enrolledDeviceRecord.id}/approve`, {
    method: "POST",
    token: adminToken,
    expectedStatus: 412
  });
  assert(approveDenied.payload.errorCode === "step_up_required", "Expected device approval to require step-up.");

  const steppedUpAdmin = await request(`${controlPlaneApi}/auth/step-up`, {
    method: "POST",
    token: adminToken,
    body: {
      password: currentAdminPassword
    }
  });
  requireCorrelationHeader(steppedUpAdmin, "admin step-up");

  const approvedDevice = await request(`${controlPlaneApi}/devices/${enrolledDeviceRecord.id}/approve`, {
    method: "POST",
    token: adminToken
  });
  requireCorrelationHeader(approvedDevice, "device approve");

  await request(`${controlPlaneApi}/auth/client/devices/${enrolledDeviceRecord.id}/posture`, {
    method: "POST",
    token: userToken,
    body: {
      deviceId: enrolledDeviceRecord.id,
      managed: true,
      compliant: true,
      bitLockerEnabled: true,
      defenderHealthy: true,
      firewallEnabled: true,
      secureBootEnabled: true,
      tamperProtectionEnabled: true,
      osVersion: "Windows 11 24H2",
      tenantId: enrolledDeviceRecord.tenantId,
      schemaVersion: 1
    }
  });

  const clientSession = await request(`${controlPlaneApi}/auth/client/session`, {
    method: "POST",
    token: userToken,
    body: {
      deviceId: enrolledDeviceRecord.id
    }
  });
  requireCorrelationHeader(clientSession, "client session issue");
  assert(clientSession.payload.bundle.policyIds.length > 0, "Issued client session did not include a policy bundle.");

  const deviceDiagnostics = await request(`${controlPlaneApi}/devices/${enrolledDeviceRecord.id}/diagnostics`, {
    token: adminToken
  });
  requireCorrelationHeader(deviceDiagnostics, "device diagnostics");

  const tempAdminUsername = `readonly-${randomUUID().slice(0, 8)}`;
  const createdAdmin = await request(`${controlPlaneApi}/admins`, {
    method: "POST",
    token: adminToken,
    body: {
      id: null,
      username: tempAdminUsername,
      password: "TempAdmin!234",
      role: "ReadOnly",
      mustChangePassword: false,
      mfaEnrolled: true
    }
  });
  await request(`${controlPlaneApi}/admins/${createdAdmin.payload.id}`, {
    method: "DELETE",
    token: adminToken,
    expectedStatus: 204
  });

  const readTimings = await measureReadLatency(adminToken, `${controlPlaneApi}/operations/diagnostics`, 8);
  const readP95 = percentile(readTimings, 0.95);
  assert(readP95 < 750, `Control-plane p95 diagnostic read exceeded 750ms: ${readP95.toFixed(1)}ms`);

  const summary = {
    adminLoginMs: Number(login.durationMs.toFixed(1)),
    userLoginMs: Number(userLogin.durationMs.toFixed(1)),
    diagnosticsReadP95Ms: Number(readP95.toFixed(1)),
    gatewayCount: gateways.payload.length,
    providerCount: providers.payload.length,
    policyCount: policies.payload.length
  };

  console.log(JSON.stringify(summary, null, 2));
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
