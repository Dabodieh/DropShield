<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Model;

use DropShield\Connector\Model\ProtectedDrop;
use DropShield\Connector\Model\ProtectedDropRepository;
use DropShield\Connector\Model\ProtectedDropResolver;
use PHPUnit\Framework\TestCase;

final class ProtectedDropResolverTest extends TestCase
{
    public function testProtectedSkuRequiresAssertion(): void
    {
        $repository = $this->activeRepository(true);
        $resolver = new ProtectedDropResolver($repository);

        self::assertTrue($resolver->isProtected('pokemon-etb'));
        self::assertTrue($resolver->isProtected('POKEMON-ETB'));
    }

    public function testOrdinarySkuBypassesConnectorEnforcement(): void
    {
        $repository = $this->activeRepository(false);
        $resolver = new ProtectedDropResolver($repository);

        self::assertFalse($resolver->isProtected('regular-mug'));
    }

    public function testNoActiveDropMeansNothingIsProtected(): void
    {
        $repository = $this->createMock(ProtectedDropRepository::class);
        $repository->method('getActiveDrop')->willReturn(null);
        $resolver = new ProtectedDropResolver($repository);

        self::assertFalse($resolver->isProtected('pokemon-etb'));
    }

    public function testGetDropIdReturnsActiveDropIdentifier(): void
    {
        $resolver = new ProtectedDropResolver($this->activeRepository(true));

        self::assertSame('pokemon-etb', $resolver->getDropId());
    }

    private function activeRepository(bool $containsSku): ProtectedDropRepository
    {
        $repository = $this->createMock(ProtectedDropRepository::class);
        $drop = new ProtectedDrop(1, 'pokemon-etb', 'Pokemon', true);
        $repository->method('getActiveDrop')->willReturn($drop);
        $repository->method('activeDropContainsSku')->willReturn($containsSku);
        return $repository;
    }
}
