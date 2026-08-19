<?php

declare(strict_types=1);

namespace DropShield\Connector\Setup\Patch\Data;

use DropShield\Connector\Model\ProtectedDropRepository;
use Magento\Catalog\Model\ResourceModel\Product\CollectionFactory;
use Magento\Framework\App\Config\ScopeConfigInterface;
use Magento\Framework\Setup\ModuleDataSetupInterface;
use Magento\Framework\Setup\Patch\DataPatchInterface;

/**
 * Imports the old default-scope CSV once, if it exists. The persisted entities then become the
 * only authority; runtime code never reads legacy fields again.
 */
class MigrateLegacyProtectedDropConfiguration implements DataPatchInterface
{
    public function __construct(
        private readonly ModuleDataSetupInterface $setup,
        private readonly ScopeConfigInterface $config,
        private readonly CollectionFactory $products,
        private readonly ProtectedDropRepository $repository
    ) {
    }

    public function apply(): void
    {
        $this->setup->getConnection()->startSetup();
        try {
            if (count($this->repository->getAll()) > 0) {
                return;
            }
            $identifier = trim((string) $this->config->getValue('dropshield_connector/general/drop_id'));
            $rawSkus = (string) $this->config->getValue('dropshield_connector/general/protected_skus');
            $skus = array_values(array_unique(array_filter(array_map('trim', explode(',', $rawSkus)))));
            if ($identifier === '' || $skus === []) {
                return;
            }
            $collection = $this->products->create()->addAttributeToFilter('sku', ['in' => $skus]);
            $productIds = array_map('intval', $collection->getAllIds());
            if ($productIds === []) {
                return;
            }
            $this->repository->save(null, $identifier, 'Migrated protected drop', false, $productIds);
        } finally {
            $this->setup->getConnection()->endSetup();
        }
    }

    public static function getDependencies(): array { return []; }
    public function getAliases(): array { return []; }
}
