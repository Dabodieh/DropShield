<?php

declare(strict_types=1);

namespace DropShield\Connector\Block\Adminhtml\Drop;

use DropShield\Connector\Model\ProtectedDropRepository;
use Magento\Backend\Block\Template;
use Magento\Catalog\Model\ResourceModel\Product\CollectionFactory;

class Edit extends Template
{
    public function __construct(
        Template\Context $context,
        private readonly ProtectedDropRepository $repository,
        private readonly CollectionFactory $products,
        array $data = []
    ) {
        parent::__construct($context, $data);
    }

    public function getDrop(): ?\DropShield\Connector\Model\ProtectedDrop
    {
        $id = (int) $this->getRequest()->getParam('id');
        return $id > 0 ? $this->repository->getById($id) : null;
    }

    /** @return int[] */
    public function getAssignedProductIds(): array
    {
        return $this->getDrop() === null ? [] : $this->repository->getProductIds($this->getDrop()->entityId);
    }

    public function getProductCollection()
    {
        $collection = $this->products->create()->addAttributeToSelect(['sku', 'name']);
        $query = trim((string) $this->getRequest()->getParam('product_query'));
        if ($query !== '') {
            $collection->addAttributeToFilter([
                ['attribute' => 'sku', 'like' => '%' . $query . '%'],
                ['attribute' => 'name', 'like' => '%' . $query . '%'],
            ]);
        }
        return $collection->setPageSize(100)->setCurPage(1);
    }

    public function getFormKeyValue(): string { return $this->getFormKey(); }
}
