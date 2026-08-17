<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

final class OriginAssertionValidationResult
{
    private function __construct(
        private readonly bool $valid,
        private readonly string $failureReason
    ) {
    }

    public static function valid(): self
    {
        return new self(true, '');
    }

    public static function invalid(string $failureReason): self
    {
        return new self(false, $failureReason);
    }

    public function isValid(): bool
    {
        return $this->valid;
    }

    public function getFailureReason(): string
    {
        return $this->failureReason;
    }
}
