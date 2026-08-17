<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Model;

use DropShield\Connector\Model\AuthorizationRequiredException;
use DropShield\Connector\Model\Config;
use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\OriginAssertionValidator;
use DropShield\Connector\Model\OriginAssertionValidatorFactory;
use Magento\Framework\App\RequestInterface;
use PHPUnit\Framework\TestCase;
use Psr\Log\LoggerInterface;

final class OriginAssertionGuardTest extends TestCase
{
    public function testMissingHeaderRejectsWithGenericMessageOnly(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('isEnabled')->willReturn(true);
        $config->method('getHeaderName')->willReturn('X-DropShield-Origin-Assertion');

        $request = $this->createMock(RequestInterface::class);
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

    public function testDisabledConnectorSkipsEnforcement(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('isEnabled')->willReturn(false);

        $request = $this->createMock(RequestInterface::class);
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

        $request = $this->createMock(RequestInterface::class);
        $request->method('getHeader')->willReturn('v1.bad.bad');
        $request->method('getContent')->willReturn('{}');
        $request->method('getMethod')->willReturn('POST');

        $validator = $this->createMock(OriginAssertionValidator::class);
        $validator->method('validate')->willReturn(
            \DropShield\Connector\Model\OriginAssertionValidationResult::invalid('invalid_signature')
        );

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
