<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Model;

use DropShield\Connector\Model\Config;
use DropShield\Connector\Model\ProtectedDropResolver;
use PHPUnit\Framework\TestCase;

final class ProtectedDropResolverTest extends TestCase
{
    public function testProtectedSkuRequiresAssertion(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('getProtectedSkus')->willReturn(['pokemon-etb']);
        $resolver = new ProtectedDropResolver($config);

        self::assertTrue($resolver->isProtected('pokemon-etb'));
        self::assertTrue($resolver->isProtected('POKEMON-ETB'));
    }

    public function testOrdinarySkuBypassesConnectorEnforcement(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('getProtectedSkus')->willReturn(['pokemon-etb']);
        $resolver = new ProtectedDropResolver($config);

        self::assertFalse($resolver->isProtected('regular-mug'));
    }

    public function testEmptyProtectedListMeansNothingIsProtected(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('getProtectedSkus')->willReturn([]);
        $resolver = new ProtectedDropResolver($config);

        self::assertFalse($resolver->isProtected('pokemon-etb'));
    }

    public function testGetDropIdDelegatesToConfig(): void
    {
        $config = $this->createMock(Config::class);
        $config->method('getDropId')->willReturn('pokemon-etb');
        $resolver = new ProtectedDropResolver($config);

        self::assertSame('pokemon-etb', $resolver->getDropId());
    }
}
