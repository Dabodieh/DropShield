<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

final class ProtectedDrop
{
    public function __construct(
        public readonly int $entityId,
        public readonly string $identifier,
        public readonly string $name,
        public readonly bool $enabled
    ) {
    }
}
