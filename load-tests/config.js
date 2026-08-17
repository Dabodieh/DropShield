const DEFAULT_TARGET_BASE_URL = 'http://localhost:5058';
const SAFE_TARGET_HOSTS = [
    'localhost',
    '127.0.0.1',
    '::1',
    'host.docker.internal',
];

const PROFILES = {
    normal: {
        SMALL: { virtualUsers: 5, duration: '30s' },
        MEDIUM: { virtualUsers: 20, duration: '1m' },
        STRESS: { virtualUsers: 50, duration: '90s' },
    },
    flash: {
        SMALL: { baselineUsers: 2, peakUsers: 20, baselineDuration: '5s', rampDuration: '10s', peakDuration: '15s', cooldownDuration: '10s' },
        MEDIUM: { baselineUsers: 5, peakUsers: 50, baselineDuration: '10s', rampDuration: '15s', peakDuration: '30s', cooldownDuration: '15s' },
        STRESS: { baselineUsers: 10, peakUsers: 100, baselineDuration: '15s', rampDuration: '20s', peakDuration: '45s', cooldownDuration: '20s' },
    },
    polling: {
        SMALL: { virtualUsers: 10, duration: '30s' },
        MEDIUM: { virtualUsers: 30, duration: '45s' },
        STRESS: { virtualUsers: 60, duration: '1m' },
    },
    mixed: {
        SMALL: { stageUsers: [10, 25, 50, 75], stageDurationSeconds: 10, stageSlotSeconds: 14 },
        MEDIUM: { stageUsers: [10, 50, 150, 300], stageDurationSeconds: 15, stageSlotSeconds: 20 },
        STRESS: { stageUsers: [25, 100, 300, 600], stageDurationSeconds: 20, stageSlotSeconds: 25 },
    },
};

export const SUMMARY_TREND_STATS = [
    'avg',
    'min',
    'med',
    'p(90)',
    'p(95)',
    'p(99)',
    'max',
];

export const TARGET_BASE_URL = resolveSafeTargetBaseUrl();
export const PROFILE_NAME = resolveProfileName();
export const PROTECTED_MODE = booleanEnvironmentVariable('PROTECTED_MODE', false);

export function getProfile(scenarioName) {
    const scenarioProfiles = PROFILES[scenarioName];
    if (!scenarioProfiles) {
        throw new Error(`Unknown scenario profile group: ${scenarioName}`);
    }

    return scenarioProfiles[PROFILE_NAME];
}

export function integerEnvironmentVariable(name, fallback, minimum = 1, maximum = 10000) {
    const rawValue = __ENV[name];
    if (rawValue === undefined || rawValue === '') {
        return fallback;
    }

    if (!/^\d+$/.test(rawValue)) {
        throw new Error(`${name} must be a whole number.`);
    }

    const parsedValue = Number.parseInt(rawValue, 10);
    if (parsedValue < minimum || parsedValue > maximum) {
        throw new Error(`${name} must be between ${minimum} and ${maximum}.`);
    }

    return parsedValue;
}

export function numberEnvironmentVariable(name, fallback, minimum = 0, maximum = 1) {
    const rawValue = __ENV[name];
    if (rawValue === undefined || rawValue === '') {
        return fallback;
    }

    const parsedValue = Number(rawValue);
    if (!Number.isFinite(parsedValue) || parsedValue < minimum || parsedValue > maximum) {
        throw new Error(`${name} must be a number between ${minimum} and ${maximum}.`);
    }

    return parsedValue;
}

export function booleanEnvironmentVariable(name, fallback) {
    const rawValue = __ENV[name];
    if (rawValue === undefined || rawValue === '') {
        return fallback;
    }

    const normalizedValue = rawValue.trim().toLowerCase();
    if (normalizedValue === 'true') {
        return true;
    }

    if (normalizedValue === 'false') {
        return false;
    }

    throw new Error(`${name} must be true or false.`);
}

export function durationEnvironmentVariable(name, fallback) {
    const rawValue = __ENV[name];
    if (rawValue === undefined || rawValue === '') {
        return fallback;
    }

    if (!/^\d+(\.\d+)?(ms|s|m|h)$/.test(rawValue)) {
        throw new Error(`${name} must be a k6 duration such as 500ms, 30s, 2m, or 1h.`);
    }

    return rawValue;
}

export function randomBetween(minimum, maximum) {
    return minimum + (Math.random() * (maximum - minimum));
}

function resolveProfileName() {
    const profileName = (__ENV.PROFILE || 'SMALL').trim().toUpperCase();
    if (!['SMALL', 'MEDIUM', 'STRESS'].includes(profileName)) {
        throw new Error('PROFILE must be SMALL, MEDIUM, or STRESS.');
    }

    return profileName;
}

function resolveSafeTargetBaseUrl() {
    const rawTarget = (__ENV.TARGET_BASE_URL || DEFAULT_TARGET_BASE_URL).trim();
    const match = /^(https?):\/\/(\[[^\]]+\]|[^\/:?#]+)(?::(\d{1,5}))?\/?$/i.exec(rawTarget);
    if (!match) {
        throw new Error(
            'TARGET_BASE_URL must contain only http(s), a safe local host, and an optional port.');
    }

    const scheme = match[1].toLowerCase();
    const hostname = match[2].replace(/^\[|\]$/g, '').toLowerCase();
    const port = match[3];
    if (!SAFE_TARGET_HOSTS.includes(hostname)) {
        throw new Error(
            `Unsafe TARGET_BASE_URL host '${hostname}'. DropShield load tests permit only localhost and controlled local Docker aliases.`);
    }

    if (port && (Number.parseInt(port, 10) < 1 || Number.parseInt(port, 10) > 65535)) {
        throw new Error('TARGET_BASE_URL port must be between 1 and 65535.');
    }

    const formattedHostname = hostname === '::1' ? '[::1]' : hostname;
    return `${scheme}://${formattedHostname}${port ? `:${port}` : ''}`;
}
