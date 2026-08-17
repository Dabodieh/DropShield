<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

/**
 * Validates DropShield Origin Assertion v1 tokens.
 *
 * Pure cryptographic verifier for the wire format documented in
 * contracts/origin-assertion-v1.json. Deliberately free of any Magento
 * dependency so it can be unit tested and reasoned about in isolation.
 *
 * Format: v1.<base64url-payload>.<base64url-signature>
 */
final class OriginAssertionValidator
{
    private const VERSION = 'v1';

    public function __construct(
        private readonly string $keyId,
        private readonly string $keyMaterial,
        private readonly int $clockToleranceSeconds = 5
    ) {
    }

    /**
     * @return OriginAssertionValidationResult
     */
    public function validate(
        string $assertion,
        string $expectedDrop,
        string $expectedAction,
        string $expectedMethod,
        string $expectedRoute,
        string $rawBody,
        ?int $now = null
    ): OriginAssertionValidationResult {
        if ($assertion === '' || strlen($assertion) > 2048) {
            return OriginAssertionValidationResult::invalid('malformed');
        }

        $parts = explode('.', $assertion);
        if (count($parts) !== 3 || $parts[0] !== self::VERSION) {
            return OriginAssertionValidationResult::invalid('unsupported_version');
        }

        [, $payloadPart, $signaturePart] = $parts;

        $payloadBytes = self::base64UrlDecode($payloadPart);
        $actualSignature = self::base64UrlDecode($signaturePart);
        if ($payloadBytes === null || $actualSignature === null) {
            return OriginAssertionValidationResult::invalid('malformed');
        }

        $payload = json_decode($payloadBytes, true);
        if (!is_array($payload) || !self::hasRequiredFields($payload)) {
            return OriginAssertionValidationResult::invalid('malformed');
        }

        if ((int) $payload['v'] !== 1) {
            return OriginAssertionValidationResult::invalid('unsupported_version');
        }

        if (!hash_equals($this->keyId, (string) $payload['kid'])) {
            return OriginAssertionValidationResult::invalid('unknown_key_id');
        }

        $signingInput = self::VERSION . '.' . $payloadPart;
        $expectedSignature = hash_hmac('sha256', $signingInput, $this->keyMaterial, true);
        if (!hash_equals($expectedSignature, $actualSignature)) {
            return OriginAssertionValidationResult::invalid('invalid_signature');
        }

        $iat = (int) $payload['iat'];
        $exp = (int) $payload['exp'];
        $currentTime = $now ?? time();
        $lifetime = $exp - $iat;
        if ($iat < 0 || $exp <= $iat || $lifetime > (3600) ||
            $currentTime >= ($exp + $this->clockToleranceSeconds)) {
            return OriginAssertionValidationResult::invalid('expired');
        }

        if (strtoupper((string) $payload['method']) !== strtoupper($expectedMethod)) {
            return OriginAssertionValidationResult::invalid('wrong_method');
        }

        if (!hash_equals((string) $payload['route'], $expectedRoute)) {
            return OriginAssertionValidationResult::invalid('wrong_route');
        }

        if (!hash_equals((string) $payload['drop'], $expectedDrop)) {
            return OriginAssertionValidationResult::invalid('wrong_drop');
        }

        if (!hash_equals((string) $payload['action'], $expectedAction)) {
            return OriginAssertionValidationResult::invalid('wrong_action');
        }

        $expectedBodyHash = hash('sha256', $rawBody, true);
        $actualBodyHash = self::base64UrlDecode((string) $payload['bodyHash']);
        if ($actualBodyHash === null || !hash_equals($expectedBodyHash, $actualBodyHash)) {
            return OriginAssertionValidationResult::invalid('body_mismatch');
        }

        return OriginAssertionValidationResult::valid();
    }

    private static function hasRequiredFields(array $payload): bool
    {
        foreach (['v', 'kid', 'drop', 'action', 'method', 'route', 'bodyHash', 'jti', 'iat', 'exp'] as $field) {
            if (!array_key_exists($field, $payload)) {
                return false;
            }
        }

        return true;
    }

    private static function base64UrlDecode(string $value): ?string
    {
        if ($value === '' || preg_match('/^[A-Za-z0-9_-]+$/', $value) !== 1) {
            return null;
        }

        $base64 = strtr($value, '-_', '+/');
        $padding = strlen($base64) % 4;
        if ($padding === 1) {
            return null;
        }

        if ($padding > 0) {
            $base64 .= str_repeat('=', 4 - $padding);
        }

        $decoded = base64_decode($base64, true);

        return $decoded === false ? null : $decoded;
    }
}
