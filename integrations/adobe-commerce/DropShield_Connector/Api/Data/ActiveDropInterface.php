<?php

declare(strict_types=1);

namespace DropShield\Connector\Api\Data;

/**
 * The currently enabled protected drop and its assigned products, as exposed by the
 * protection manifest.
 */
interface ActiveDropInterface
{
    /**
     * @return string
     */
    public function getId(): string;

    /**
     * @return \DropShield\Connector\Api\Data\ProtectedProductInterface[]
     */
    public function getProducts(): array;
}
