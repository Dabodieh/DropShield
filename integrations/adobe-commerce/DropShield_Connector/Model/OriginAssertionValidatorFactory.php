<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

/**
 * Builds a configured OriginAssertionValidator from store configuration,
 * keeping Magento-specific configuration lookup out of the crypto class.
 */
class OriginAssertionValidatorFactory
{
    public function __construct(private readonly Config $config)
    {
    }

    public function create(?int $storeId = null): OriginAssertionValidator
    {
        $keyMaterial = base64_decode($this->config->getSigningKeyBase64($storeId), true);
        if ($keyMaterial === false || strlen($keyMaterial) < 32) {
            throw new \RuntimeException(
                'DropShield origin assertion signing key is not configured or is too short.'
            );
        }

        return new OriginAssertionValidator(
            $this->config->getKeyId($storeId),
            $keyMaterial,
            $this->config->getClockToleranceSeconds($storeId)
        );
    }
}
