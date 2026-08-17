<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Model;

use DropShield\Connector\Model\AuthorizationRequiredException;
use DropShield\Connector\Model\Config;
use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\OriginAssertionValidator;
use DropShield\Connector\Model\OriginAssertionValidatorFactory;
use Magento\Framework\App\Request\Http as HttpRequest;
use PHPUnit\Framework\TestCase;
use Psr\Log\LoggerInterface;

final class OriginAssertionGuardTest extends TestCase
{
    public function testMissingHeaderRejectsWithGenericMessageOnly(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('isEnabled')->willReturn(true);
        $config->method('getHeaderName')->willReturn('X-DropShield-Origin-Assertion');

        $request = $this->createMock(HttpRequest::class);
        $request->method('getHeader')->willReturn(false);

        $factory = $this->createMock(OriginAssertionValidatorFactory::class);
        $logger = $this->createMock(LoggerInterface::class);

        $guard = new OriginAssertionGuard($config, $factory, $logger);

        try {
            $guard->requireValidAssertion($request, 'pokemon-etb', 'cart', 'POST /api/cart');
            self::fail('Expected AuthorizationRequiredException.');
        } catch (AuthorizationRequiredException $exception) {
            self::assertSame('DropShield authorization required.', $exception->getMessage());
        }
    }

    public function testValidAssertionAllowsRequest(): void
    {
        $keyId = 'test-key-1';
        $keyMaterial = str_repeat("\x01", 32);
        $body = '{}';
        $assertion = $this->signAssertion($keyId, $keyMaterial, 'pokemon-etb', 'cart', 'POST', 'POST /api/cart', $body);

        $config = $this->createMock(Config::class);
        $config->method('isEnabled')->willReturn(true);
        $config->method('getHeaderName')->willReturn('X-DropShield-Origin-Assertion');

        $request = $this->createMock(HttpRequest::class);
        $request->method('getHeader')->willReturn($assertion);
        $request->method('getContent')->willReturn($body);
        $request->method('getMethod')->willReturn('POST');

        $validator = new OriginAssertionValidator($keyId, $keyMaterial);
        $factory = $this->createMock(OriginAssertionValidatorFactory::class);
        $factory->method('create')->willReturn($validator);
        $logger = $this->createMock(LoggerInterface::class);

        $guard = new OriginAssertionGuard($config, $factory, $logger);

        $guard->requireValidAssertion($request, 'pokemon-etb', 'cart', 'POST /api/cart');
        $this->addToAssertionCount(1);
    }

    private function signAssertion(
        string $keyId,
        string $keyMaterial,
        string $drop,
        string $action,
        string $method,
        string $route,
        string $body
    ): string {
        $now = time();
        $payload = json_encode([
            'v' => 1,
            'kid' => $keyId,
            'drop' => $drop,
            'action' => $action,
            'method' => $method,
            'route' => $route,
            'bodyHash' => $this->base64UrlEncode(hash('sha256', $body, true)),
            'jti' => $this->base64UrlEncode(random_bytes(16)),
            'iat' => $now,
            'exp' => $now + 20,
        ], JSON_THROW_ON_ERROR);

        $payloadPart = $this->base64UrlEncode($payload);
        $signingInput = "v1.$payloadPart";
        $signature = hash_hmac('sha256', $signingInput, $keyMaterial, true);

        return $signingInput . '.' . $this->base64UrlEncode($signature);
    }

    private function base64UrlEncode(string $value): string
    {
        return rtrim(strtr(base64_encode($value), '+/', '-_'), '=');
    }

    public function testDisabledConnectorSkipsEnforcement(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('isEnabled')->willReturn(false);

        $request = $this->createMock(HttpRequest::class);
        $factory = $this->createMock(OriginAssertionValidatorFactory::class);
        $logger = $this->createMock(LoggerInterface::class);

        $guard = new OriginAssertionGuard($config, $factory, $logger);

        $guard->requireValidAssertion($request, 'pokemon-etb', 'cart', 'POST /api/cart');
        $this->addToAssertionCount(1);
    }

    public function testInvalidAssertionRejectsWithGenericMessageOnly(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('isEnabled')->willReturn(true);
        $config->method('getHeaderName')->willReturn('X-DropShield-Origin-Assertion');

        $request = $this->createMock(HttpRequest::class);
        $request->method('getHeader')->willReturn('v1.bad.bad');
        $request->method('getContent')->willReturn('{}');
        $request->method('getMethod')->willReturn('POST');

        $validator = new OriginAssertionValidator('test-key-1', str_repeat("\x02", 32));
        $factory = $this->createMock(OriginAssertionValidatorFactory::class);
        $factory->method('create')->willReturn($validator);
        $logger = $this->createMock(LoggerInterface::class);

        $guard = new OriginAssertionGuard($config, $factory, $logger);

        try {
            $guard->requireValidAssertion($request, 'pokemon-etb', 'cart', 'POST /api/cart');
            self::fail('Expected AuthorizationRequiredException.');
        } catch (AuthorizationRequiredException $exception) {
            self::assertSame('DropShield authorization required.', $exception->getMessage());
            self::assertStringNotContainsString('v1.bad.bad', $exception->getMessage());
        }
    }
}
