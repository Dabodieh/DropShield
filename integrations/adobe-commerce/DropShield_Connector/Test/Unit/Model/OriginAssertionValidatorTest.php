<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Model;

use DropShield\Connector\Model\OriginAssertionValidator;
use PHPUnit\Framework\TestCase;

/**
 * Validates the pure crypto verifier against the deterministic
 * cross-language test vector in contracts/origin-assertion-v1.json,
 * plus tamper/negative cases. No Magento framework dependency required.
 */
final class OriginAssertionValidatorTest extends TestCase
{
    private const KEY_ID = 'test-key-1';
    private const KEY_BASE64 = 'dGVzdC1vbmx5LW9yaWdpbi1hc3NlcnRpb24ta2V5LTAwMDAwMDAwMDA=';
    private const DROP = 'pokemon-etb';
    private const ACTION = 'cart';
    private const METHOD = 'POST';
    private const ROUTE = 'POST /api/cart';
    private const BODY = '{"productId":"pokemon-etb","quantity":1}';
    private const ASSERTION = 'v1.eyJ2IjoxLCJraWQiOiJ0ZXN0LWtleS0xIiwiZHJvcCI6InBva2Vtb24tZXRiIiwiYWN0aW9uIjoiY2FydCIsIm1ldGhvZCI6IlBPU1QiLCJyb3V0ZSI6IlBPU1QgL2FwaS9jYXJ0IiwiYm9keUhhc2giOiJvSkVXNGpncTFTNWRROGxVd1VwODlBWTdxYVBGM2lGdEVxTkdTTW55Q3AwIiwianRpIjoiQUFFQ0F3UUZCZ2NJQ1FvTERBME9EdyIsImlhdCI6MTc1NTQzMjAwMCwiZXhwIjoxNzU1NDMyMDIwfQ._8K7IlF3kXGEexqo6wb0FSwxvIBkvCvhPwCxlOL8pY0';
    private const VERIFICATION_TIME = 1755432010;

    public function testKnownCSharpTestVectorValidates(): void
    {
        $validator = $this->createValidator();

        $result = $validator->validate(
            self::ASSERTION,
            self::DROP,
            self::ACTION,
            self::METHOD,
            self::ROUTE,
            self::BODY,
            self::VERIFICATION_TIME
        );

        self::assertTrue($result->isValid());
    }

    public function testInvalidSignatureFails(): void
    {
        $validator = $this->createValidator();
        $parts = explode('.', self::ASSERTION);
        $signatureBytes = self::base64UrlDecode($parts[2]);
        $signatureBytes[0] = chr(ord($signatureBytes[0]) ^ 0xFF);
        $parts[2] = self::base64UrlEncode($signatureBytes);
        $tampered = implode('.', $parts);

        $result = $validator->validate(
            $tampered,
            self::DROP,
            self::ACTION,
            self::METHOD,
            self::ROUTE,
            self::BODY,
            self::VERIFICATION_TIME
        );

        self::assertFalse($result->isValid());
        self::assertSame('invalid_signature', $result->getFailureReason());
    }

    public function testExpiredAssertionFails(): void
    {
        $validator = $this->createValidator();

        $result = $validator->validate(
            self::ASSERTION,
            self::DROP,
            self::ACTION,
            self::METHOD,
            self::ROUTE,
            self::BODY,
            1755432200
        );

        self::assertFalse($result->isValid());
        self::assertSame('expired', $result->getFailureReason());
    }

    public function testWrongMethodFails(): void
    {
        $validator = $this->createValidator();

        $result = $validator->validate(
            self::ASSERTION,
            self::DROP,
            self::ACTION,
            'PUT',
            self::ROUTE,
            self::BODY,
            self::VERIFICATION_TIME
        );

        self::assertFalse($result->isValid());
        self::assertSame('wrong_method', $result->getFailureReason());
    }

    public function testWrongActionFails(): void
    {
        $validator = $this->createValidator();

        $result = $validator->validate(
            self::ASSERTION,
            self::DROP,
            'checkout',
            self::METHOD,
            self::ROUTE,
            self::BODY,
            self::VERIFICATION_TIME
        );

        self::assertFalse($result->isValid());
        self::assertSame('wrong_action', $result->getFailureReason());
    }

    public function testWrongDropFails(): void
    {
        $validator = $this->createValidator();

        $result = $validator->validate(
            self::ASSERTION,
            'another-drop',
            self::ACTION,
            self::METHOD,
            self::ROUTE,
            self::BODY,
            self::VERIFICATION_TIME
        );

        self::assertFalse($result->isValid());
        self::assertSame('wrong_drop', $result->getFailureReason());
    }

    public function testBodyMismatchFailsWhenBound(): void
    {
        $validator = $this->createValidator();

        $result = $validator->validate(
            self::ASSERTION,
            self::DROP,
            self::ACTION,
            self::METHOD,
            self::ROUTE,
            '{"productId":"other-item"}',
            self::VERIFICATION_TIME
        );

        self::assertFalse($result->isValid());
        self::assertSame('body_mismatch', $result->getFailureReason());
    }

    public function testFailureReasonNeverExposesSecretOrSignatureMaterial(): void
    {
        $validator = $this->createValidator();

        $result = $validator->validate(
            'v1.bad.bad',
            self::DROP,
            self::ACTION,
            self::METHOD,
            self::ROUTE,
            self::BODY,
            self::VERIFICATION_TIME
        );

        self::assertFalse($result->isValid());
        self::assertStringNotContainsString(self::KEY_BASE64, $result->getFailureReason());
        self::assertStringNotContainsString(self::ASSERTION, $result->getFailureReason());
    }

    private function createValidator(): OriginAssertionValidator
    {
        $keyMaterial = base64_decode(self::KEY_BASE64, true);
        self::assertIsString($keyMaterial);

        return new OriginAssertionValidator(self::KEY_ID, $keyMaterial, 5);
    }

    private static function base64UrlDecode(string $value): string
    {
        $base64 = strtr($value, '-_', '+/');
        $padding = strlen($base64) % 4;
        if ($padding > 0) {
            $base64 .= str_repeat('=', 4 - $padding);
        }

        return base64_decode($base64, true);
    }

    private static function base64UrlEncode(string $value): string
    {
        return rtrim(strtr(base64_encode($value), '+/', '-_'), '=');
    }
}
