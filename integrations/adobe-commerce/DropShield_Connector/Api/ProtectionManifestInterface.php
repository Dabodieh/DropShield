<?php

declare(strict_types=1);

namespace DropShield\Connector\Api;

use DropShield\Connector\Api\Data\ManifestInterface;

interface ProtectionManifestInterface
{
    /**
     * Returns only active-drop product identifiers and SKUs; no catalogue, customer, or secret data.
     *
     * @return \DropShield\Connector\Api\Data\ManifestInterface
     */
    public function get(): ManifestInterface;
}
