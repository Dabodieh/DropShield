<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

use Magento\Framework\App\Config\ScopeConfigInterface;
use Magento\Framework\Encryption\EncryptorInterface;
use Magento\Store\Model\ScopeInterface;

/**
 * Reads DropShield_Connector system configuration (stores/config -> dropshield/connector).
 */
class Config
{
    private const XML_PATH_ENABLED = 'dropshield_connector/general/enabled';
    private const XML_PATH_DROP_ID = 'dropshield_connector/general/drop_id';
    private const XML_PATH_PROTECTED_SKUS = 'dropshield_connector/general/protected_skus';
    private const XML_PATH_KEY_ID = 'dropshield_connector/origin_assertion/key_id';
    private const XML_PATH_SIGNING_KEY = 'dropshield_connector/origin_assertion/signing_key';
    private const XML_PATH_CLOCK_TOLERANCE_SECONDS = 'dropshield_connector/origin_assertion/clock_tolerance_seconds';
    private const XML_PATH_HEADER_NAME = 'dropshield_connector/origin_assertion/header_name';

    public function __construct(
        private readonly ScopeConfigInterface $scopeConfig,
        private readonly EncryptorInterface $encryptor
    ) {
    }

    public function isEnabled(?int $storeId = null): bool
    {
        return (bool) $this->scopeConfig->isSetFlag(
            self::XML_PATH_ENABLED,
            ScopeInterface::SCOPE_STORE,
            $storeId
        );
    }

    /**
     * The single active protected drop identifier. Must match DropShield.Api's
     * Admission:ProtectedProduct — DropShield signs origin assertions for exactly one drop,
     * and every protected SKU (see getProtectedSkus()) maps to it.
     */
    public function getDropId(?int $storeId = null): string
    {
        return (string) $this->scopeConfig->getValue(
            self::XML_PATH_DROP_ID,
            ScopeInterface::SCOPE_STORE,
            $storeId
        );
    }

    /**
     * @return string[]
     */
    public function getProtectedSkus(?int $storeId = null): array
    {
        $raw = (string) $this->scopeConfig->getValue(
            self::XML_PATH_PROTECTED_SKUS,
            ScopeInterface::SCOPE_STORE,
            $storeId
        );

        $skus = array_filter(array_map('trim', explode(',', $raw)), static fn (string $sku): bool => $sku !== '');

        return array_values($skus);
    }

    public function getKeyId(?int $storeId = null): string
    {
        return (string) $this->scopeConfig->getValue(
            self::XML_PATH_KEY_ID,
            ScopeInterface::SCOPE_STORE,
            $storeId
        );
    }

    /**
     * Config field uses the Encrypted backend model for admin display/storage, but
     * ScopeConfigInterface::getValue() returns the raw encrypted string — decryption only
     * happens automatically inside the admin config form's own load path. Every other reader
     * of an Encrypted-backed value (this connector included) must decrypt explicitly.
     */
    public function getSigningKeyBase64(?int $storeId = null): string
    {
        $encrypted = (string) $this->scopeConfig->getValue(
            self::XML_PATH_SIGNING_KEY,
            ScopeInterface::SCOPE_STORE,
            $storeId
        );

        return $encrypted === '' ? '' : $this->encryptor->decrypt($encrypted);
    }

    public function getClockToleranceSeconds(?int $storeId = null): int
    {
        $value = (int) $this->scopeConfig->getValue(
            self::XML_PATH_CLOCK_TOLERANCE_SECONDS,
            ScopeInterface::SCOPE_STORE,
            $storeId
        );

        return $value > 0 ? $value : 5;
    }

    public function getHeaderName(?int $storeId = null): string
    {
        $value = (string) $this->scopeConfig->getValue(
            self::XML_PATH_HEADER_NAME,
            ScopeInterface::SCOPE_STORE,
            $storeId
        );

        return $value !== '' ? $value : 'X-DropShield-Origin-Assertion';
    }
}
