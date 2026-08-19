<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

use Magento\Framework\App\ResourceConnection;
use Magento\Framework\Exception\LocalizedException;

/**
 * Small persistence boundary for protected-drop configuration. Catalogue fields are deliberately
 * never copied here: assignments retain only Magento product entity IDs.
 */
class ProtectedDropRepository
{
    private const DROP_TABLE = 'dropshield_protected_drop';
    private const ASSIGNMENT_TABLE = 'dropshield_protected_drop_product';
    private const LOCK_TABLE = 'dropshield_protected_drop_lock';
    public const MAX_PRODUCTS_PER_DROP = 10000;

    public function __construct(private readonly ResourceConnection $resource)
    {
    }

    public function getActiveDrop(): ?ProtectedDrop
    {
        $connection = $this->resource->getConnection();
        $rows = $connection->fetchAll(
            $connection->select()->from($this->table(self::DROP_TABLE))->where('is_enabled = ?', 1)->limit(2)
        );

        // Fail safely if a damaged database somehow violates the invariant.
        if (count($rows) !== 1) {
            return null;
        }

        return $this->hydrate($rows[0]);
    }

    /** @return ProtectedDrop[] */
    public function getAll(): array
    {
        $connection = $this->resource->getConnection();
        return array_map(
            fn (array $row): ProtectedDrop => $this->hydrate($row),
            $connection->fetchAll($connection->select()->from($this->table(self::DROP_TABLE))->order('updated_at DESC'))
        );
    }

    public function getById(int $entityId): ?ProtectedDrop
    {
        $row = $this->resource->getConnection()->fetchRow(
            $this->resource->getConnection()->select()->from($this->table(self::DROP_TABLE))->where('entity_id = ?', $entityId)
        );
        return $row === false ? null : $this->hydrate($row);
    }

    /** @return int[] */
    public function getProductIds(int $entityId): array
    {
        return array_map('intval', $this->resource->getConnection()->fetchCol(
            $this->resource->getConnection()->select()->from($this->table(self::ASSIGNMENT_TABLE), 'product_id')->where('drop_id = ?', $entityId)
        ));
    }

    /** @param int[] $productIds */
    public function save(?int $entityId, string $identifier, string $name, bool $enabled, array $productIds): int
    {
        $identifier = trim($identifier);
        $name = trim($name);
        $productIds = array_values(array_unique(array_map('intval', $productIds)));
        if (!preg_match('/^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/D', $identifier)) {
            throw new LocalizedException(__('Drop ID must contain 1-64 letters, numbers, dots, underscores, or hyphens.'));
        }
        if ($name === '' || mb_strlen($name) > 255) {
            throw new LocalizedException(__('Name is required and must be at most 255 characters.'));
        }
        if (count($productIds) > self::MAX_PRODUCTS_PER_DROP || count(array_filter($productIds, static fn (int $id): bool => $id <= 0)) > 0) {
            throw new LocalizedException(__('The selected product set is invalid or exceeds the supported limit.'));
        }
        if ($enabled && count($productIds) === 0) {
            throw new LocalizedException(__('An enabled protected drop must contain at least one product.'));
        }

        $connection = $this->resource->getConnection();
        $connection->beginTransaction();
        try {
            $this->lockEnabledDropInvariant();
            if ($enabled) {
                $activeId = $connection->fetchOne(
                    $connection->select()->from($this->table(self::DROP_TABLE), 'entity_id')->where('is_enabled = ?', 1)->limit(1)
                );
                if ($activeId !== false && (int) $activeId !== $entityId) {
                    throw new LocalizedException(__('Another DropShield protected drop is already enabled.'));
                }
            }

            $data = ['drop_identifier' => $identifier, 'name' => $name, 'is_enabled' => (int) $enabled];
            if ($entityId === null) {
                $connection->insert($this->table(self::DROP_TABLE), $data);
                $entityId = (int) $connection->lastInsertId($this->table(self::DROP_TABLE));
            } else {
                $connection->update($this->table(self::DROP_TABLE), $data, ['entity_id = ?' => $entityId]);
            }

            $connection->delete($this->table(self::ASSIGNMENT_TABLE), ['drop_id = ?' => $entityId]);
            foreach ($productIds as $productId) {
                $connection->insert($this->table(self::ASSIGNMENT_TABLE), ['drop_id' => $entityId, 'product_id' => $productId]);
            }
            $connection->commit();
            return $entityId;
        } catch (\Throwable $exception) {
            $connection->rollBack();
            throw $exception;
        }
    }

    public function delete(int $entityId): void
    {
        $connection = $this->resource->getConnection();
        $connection->beginTransaction();
        try {
            $this->lockEnabledDropInvariant();
            $drop = $this->getById($entityId);
            if ($drop !== null && $drop->enabled) {
                throw new LocalizedException(__('Disable the protected drop before deleting it.'));
            }
            $connection->delete($this->table(self::DROP_TABLE), ['entity_id = ?' => $entityId]);
            $connection->commit();
        } catch (\Throwable $exception) {
            $connection->rollBack();
            throw $exception;
        }
    }

    /** @return array<int, array{product_id:int,sku:string}> */
    public function getActiveProducts(): array
    {
        $drop = $this->getActiveDrop();
        if ($drop === null) {
            return [];
        }
        $connection = $this->resource->getConnection();
        return $connection->fetchAll(
            $connection->select()
                ->from(['assignment' => $this->table(self::ASSIGNMENT_TABLE)], ['product_id'])
                ->joinInner(['product' => $this->table('catalog_product_entity')], 'product.entity_id = assignment.product_id', ['sku'])
                ->where('assignment.drop_id = ?', $drop->entityId)
                ->order('assignment.product_id ASC')
        );
    }

    public function activeDropContainsSku(ProtectedDrop $drop, string $sku): bool
    {
        $connection = $this->resource->getConnection();
        return $connection->fetchOne(
            $connection->select()
                ->from(['assignment' => $this->table(self::ASSIGNMENT_TABLE)], ['product_id'])
                ->joinInner(['product' => $this->table('catalog_product_entity')], 'product.entity_id = assignment.product_id', [])
                ->where('assignment.drop_id = ?', $drop->entityId)
                ->where('product.sku = ?', $sku)
                ->limit(1)
        ) !== false;
    }

    private function lockEnabledDropInvariant(): void
    {
        $connection = $this->resource->getConnection();
        $table = $this->table(self::LOCK_TABLE);
        $connection->insertOnDuplicate($table, ['lock_id' => 1], ['lock_id']);
        $connection->query(sprintf('SELECT lock_id FROM %s WHERE lock_id = 1 FOR UPDATE', $connection->quoteIdentifier($table)));
    }

    private function table(string $name): string
    {
        return $this->resource->getTableName($name);
    }

    /** @param array<string, mixed> $row */
    private function hydrate(array $row): ProtectedDrop
    {
        return new ProtectedDrop((int) $row['entity_id'], (string) $row['drop_identifier'], (string) $row['name'], (bool) $row['is_enabled']);
    }
}
